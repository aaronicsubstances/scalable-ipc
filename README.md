# ScalableIPC Protocol

Specifies an OS-independent network protocol for efficient inter-process communication between processes which
   
   1. Co-exist on the same host  machine (using UDP).
   2. Separately exist on different hosts on the Internet (using TCP/TLS).

The initial motivation for this protocol came from deliberations on IPC efficiency between microservice-based HTTP-based applications.

## Convenient Features

  * Leverages existing network protocols used on the Internet.
  * Supports both client/server and peer-to-peer modes.
  * Preservation of message boundaries, making it directly useful for protocol use cases characterised by request-response exchanges and single-message data transfers.
  * Connectionless, in the sense that the end user can assume the communication endpoints are always available, and that errors arising from any underlying transport's connections are transient.
  * Completely takes care of connection management in underlying transport for end users.
  * Reliable delivery, even if underlying transport doesn't offer reliability.
  * Efficient data transfer by multiplexing multiple messages over a single underlying transport's connection.
  * Designed to work with blocking or non-blocking I/O using callbacks.
  * Designed to work in single or multithreaded environments using [Single-threaded Event-driven Frameworks](http://berb.github.io/diploma-thesis/original/055_events.html#st).
       - A workaround for C#.NET Core (applicable to Java as well) is available [here](https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskscheduler?view=netcore-3.1).
       - A workaround for PHP is available [here](https://reactphp.org).

## Protocol Specification

*NB*: The keywords “MUST”, “MUST NOT”, “REQUIRED”, “SHALL”, “SHALL NOT”, “SHOULD”, “SHOULD NOT”, “RECOMMENDED”, “MAY”, and “OPTIONAL” in this section are to be interpreted as described in [RFC 2119](http://tools.ietf.org/html/rfc2119).

### Design Decisions

  * Use UDP as the standard transport for same host communications.
  * Use TCP/TLS as the standard transport for communications on the Internet.
  * Assume underlying transport is sufficiently secure and detects/prevents data corruption on its own. Protocol's part is to deal with dropped, delayed and/or duplicated PDUs.
  * Also assume underlying transport handles congestion on its own.
  * Simple error control by continuously retrying within a fixed time period with random backoff strategy. Underlying transport is at liberty to use more complicated backoff strategies behind the scenes.
  * Simple flow control with stop-and-wait. Underlying transport is at liberty to transparently use more complicated flow control mechanisms behind the scenes together with fragmenting PDUs.
  * Use non-negotiable configuration parameters:
       - Maximum receivable message size. Must be at least 64 kilobytes (65536 bytes).
       - Endpoint owner id. Necessary for preventing repeated data processing during process restarts. Must be changed periodically during lifetime of the process which creates network endpoint.
       - Time delay in between resets of endpoint owner ids and cleaning up received message ids.
       - Receive timeout of data PDUs. Necessary for cleaning up abandoned data transfers.
       - Maximum transfer unit (MTU). Must be at least 512.
       - Minimum and maximum random backoff period between retries.
       - Receive timeout of acknowledgment PDUs. This becomes the time available for retries.


### PDU Structure

In the case of UDP, the datagram length indicates the length of the PDU.
In the case of TCP/TLS, the PDU must be wrapped in a TLV format.

message length, sequence number are signed 32-bit big endian integers. error code is signed 16-bit big endian integer.

sequence number, protocol version, opcode, message ids, message source and destination ids, cannot be all zeros.

all strings are utf8-encoded.

opcodes
   - header
   - header_ack
   - data
   - data_ack

Beginning members
   - opcode - 1 byte
   - protocol version - 1 byte
   - reserved - 4 bytes
   - message id - 16 bytes (uuid)

HEADER members
   - message destination id - 16 bytes (uuid)
   - message length - 4 bytes
   - (payload, can be empty)

HEADER_ACK members
   - message source id - 16 bytes (uuid)
   - error code - 2 bytes

DATA members
   - message destination id - 16 bytes (uuid)
   - sequence number - 4 bytes
   - (payload, can be empty)

DATA_ACK members
   - message source id - 16 bytes (uuid)
   - sequence number - 4 bytes
   - error code - 2 bytes

### Receive Operation

#### State

  1. receive buffer 
  2. filled length
  1. is processed
  2. message source id
  1. expected seq nr
  2. last ack sent
  3. receive data handler

#### Events

  1. header pdu received
  2. data pdu received
  1. receive data timer elapses
  2. reset
  1. reset endpoint owner id timer elapses 

#### Details

NB:
  1. if a header or data pdu is received such that its addition will cause the message length to be exceeded, then receiver must pick the prefix of the pdu required to satisfy the message length.


##### header pdu received

assert that message id is NOT already processed. ignore if otherwise, except  in the case where the expected seq nr is 0 AND message destination id matches msg src id. in that case send back the last ack sent (or construct one for aborted cases).

assert that message id doesn't exist. ignore if otherwise, except in the case where expected seq nr is 1. in that case send back the last ack sent.

assert that message length is within maximum. 0 is allowed. reply with error code if otherwise.

assert that if pdu isn't the last pdu of message, then the data size is at least 512. ignore if otherwise.

assert that pdu data size is within maximum. reply with error code if otherwise.

last assert before success is that message destination id matches endpoint owner id. reply with invalid msg dest id error code is otherwise.

create receive buffer of length the full message length. fill receive buffer with pdu data, and reply with ack. don't wait for ack send outcome.

save endpoint owner id as message source id, just in case a future endpoint owner reset changes it.

if receive buffer is full, notify receive data handler with message id and receive buffer contents, and mark message id as processed.

else set expected seq nr to 1 and set receive data timer.

##### data pdu received

assert that message id is NOT already processed. ignore if otherwise, except  in the case where the expected seq nr is same as data pdu's seq nr AND message destination id matches msg src id. in that case send back the last ack sent (or construct one for aborted cases).

assert that message id exists. ignore if otherwise.

assert that pdu seq nr matches expected seq nr. ignore, except except in the case where expected seq nr is 1 more than pdu seq nr. in that case send back the last ack sent.

assert that pdu data size is within maximum. reply with error code if otherwise.

last assert before success is that message destination id matches msg src id. reply with invalid msg dest id error code is otherwise.

if pdu isn't the last pdu of message, and data size is less than 512, interpret that as intention by sender to abort transfer, and abort receive.

fill receive buffer with pdu data, and reply with ack. don't wait for ack send outcome.

if receive buffer is full, notify receive data handler with message id and receive buffer contents, and mark message id as processed.

else increment expected seq nr by 1 and reset receive data timer.

##### receive data timer elapses, reset

abort receive transfer by cancelling receive data timer.

##### reset endpoint owner id timer elapses

Eject all processed received message ids. Change endpoint owner id.

### Send Operation

#### State

  1. message id
  2. remote endpoint
  1. send request callback
  2. fragmented message
  1. message destination id
  2. index of sent pdu whose ack is pending

#### Events
  
  1. message send request received
  2. header ack received
  1. data ack received
  2. sent pdu callback invoked
  1. retry backoff timer elapses
  2. receive ack timer elapses
  1. reset
  2. expiration timer elapses for known message destination id association

#### Details

##### message send request received

if message send request is received for a remote endpoint, save any send request callback supplied.

Generate an id for the message.

Fragment the message into pdus into array of 1 header pdu and 0 or more data pdus. Ensure no pdu's data exceed preconfigured maximum limit.

Determine message destination id to use from known message destination ids for the remote endpoint, or use a random one if none exists.

Start the receive ack timer.
Send the first pdu of the message, and wait for the outcome. Ignore any send errors.

##### sent pdu callback invoked

if callback aftermath was not cancelled, then generate a random delay using preconfigured min/max backoff delays.

schedule repeat sending message of pdu at current index after delay.

##### retry backoff timer elapses

repeat sending message of pdu at current index, and waiting for outcome.

##### header ack received

assert that current pdu is the first one, and ignore if otherwise.

assert that there is no error code, and abort send transfer if there is an error code.

The exception to aborting is if error code indicates 

  1. invalid message destination id. Save the message source id of the ack as the new message destination id to use for subsequent sends. Also resend once the first pdu with the message destination id just changed. Lastly save the message destination id for use with the remote endpoint to speed up future message sends to that remote endpoint.
  2. data size too large. if pdu size used is 512, abort. Else reduce to 512, and refragment message. Also resend once the first pdu with the data size just changed.

If there is no error code, then cancel any scheduled retry, and cancel send pdu callback aftermath. Reset the receive ack timer.

Move the current pdu index to the next one. If all pdus have been sent, then message send has been successful. Invoke the message send callback.

Else send the next pdu of the message, and wait for the outcome. Ignore any send errors.

##### data ack received

assert that current pdu is not the first one, and ignore if otherwise.

assert that the sequence number of the ack equals the sequence number of the current pdu, and ignore if otherwise.

assert that there is no error code, and abort send transfer if there is an error code.

If there is no error code, then cancel any scheduled retry, and cancel send pdu callback aftermath. Reset the receive ack timer.

Move the current pdu index to the next one. If all pdus have been sent, then message send has been successful. Invoke the message send callback.

Else send the next pdu of the message, and wait for the outcome. Ignore any send errors.

##### receive ack timer elapses, reset

abort send transfer by cancelling retry backoff timer, current pdu send callback aftermath and receive ack timer. Invoke message send callback.

Send back a pdu with same fields as current pdu but with empty data to trigger an early abort at receiver. Don't wait for outcome.

##### expiration timer elapses for known message destination id association

Eject association for remote endpoint
