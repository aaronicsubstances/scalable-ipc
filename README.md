# ScalableIPC Protocol

Specifies an OS-independent network protocol for efficient inter-process communication between processes which
   
   1. Co-exist on the same host  machine (using UDP).
   2. Separately exist on different hosts on the Internet (using TCP/TLS).

The initial motivation for this protocol came from deliberations on IPC efficiency between web applications built with microservice architectures.

## Convenient Features

  * Leverages existing network protocols used on the Internet.
  * Supports both client/server and peer-to-peer modes.
  * Preservation of message boundaries, making it directly useful for protocol use cases characterised by request-response exchanges in which each request and each response is a single message.
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

opcode, reserved, payload are byte blobs.

message length, sequence number are signed 32-bit big endian integers. error code is signed 16-bit big endian integer.

sequence number, protocol version, opcode, message ids, message destination id, cannot be all zeros.

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
   - error code - 2 bytes
   - (payload, can be empty)

DATA members
   - message destination id - 16 bytes (uuid)
   - sequence number - 4 bytes
   - (payload, can be empty)

DATA_ACK members
   - sequence number - 4 bytes
   - error code - 2 bytes
   - (payload, can be empty)

NB: implementations may choose to discard ack pdus with a total size exceeding 512. *This makes 512 the de facto maximum size of data_ack and header_ack pdus.*

### Receive Operation

#### State

  1. receive buffer
  1. is processed
  2. message source id
  1. expected seq nr
  2. last ack sent
  3. receive message handler

#### Events

  1. header pdu received
  2. data pdu received
  1. receive data timer elapses
  2. reset

#### Details

NB:
  1. if a header or data pdu is received such that its addition will cause the message length to be exceeded, then receiver must pick the prefix of the pdu required to satisfy the message length.
  2. eventually discard all traces of processed received message ids anytime endpoint owner id is changed, AND processed received message id has spent as much time as the receive timeout period in processed state.
  1. never bother about failures resulting from sending ack pdus. in fact, don't even wait for the outcome if sending asynchronously.

##### header pdu received

assert in any order that

  * message id doesn't exist or expected seq nr is 0. ignore if otherwise, except in the case where expected seq nr is 1. in that case send back the last ack sent.
  * message length is within maximum. 0 is allowed. reply with error code if otherwise.
  * pdu data size is within maximum. reply with error code AND max pdu data size as the ack payload if otherwise.
  * message destination id matches endpoint owner id. reply with error code AND endpoint owner id as ack payload if otherwise.

if message id is already processed, then send back the last ack sent (or construct one for aborted cases).

create receive buffer of length the full message length. add pdu data to receive buffer, and reply with ack. don't wait for ack send outcome. set expected seq nr to 0.

save endpoint owner id as message source id, just in case a future endpoint owner reset changes it.

also save the pdu data size just in case it is changed in application.

if receive buffer is full, notify receive message handler with message id and receive buffer contents, and mark message id as processed.

else set expected seq nr to 1 and set receive data timer.

##### data pdu received

assert that message id exists. ignore if otherwise.

assert in any order that

  * pdu seq nr matches expected seq nr. ignore, except except in the case where expected seq nr is 1 more than pdu seq nr. in that case send back the last ack sent.
  * pdu data size is within maximum. reply with error code AND saved max pdu data size as ack payload if otherwise.
  * message destination id matches msg src id. reply with invalid msg dest id error code AND msg src id as ack payload if otherwise.

if message id is already processed, then send back the last ack sent (or construct one for aborted cases).

if pdu isn't the last pdu of message, and data size is 0, interpret that as intention by sender to abort transfer, and abort receive and mark message id as processed.

add pdu data to receive buffer, and reply with ack. don't wait for ack send outcome.

if receive buffer is full, notify receive data handler with message id and receive buffer contents, and mark message id as processed.

else increment expected seq nr by 1 and reset receive data timer.

##### receive data timer elapses, reset

abort receive transfer. mark message id as processed.

### Send Operation

#### State

  1. message id
  2. remote endpoint
  1. send request callback
  2. fragmented message
  1. message destination id
  2. current index (i.e. of sent pdu in fragmented message whose ack is pending)

#### Events
  
  1. message send request received
  2. header ack received
  1. data ack received
  2. sent pdu callback invoked
  1. retry backoff timer elapses
  2. receive ack timer elapses
  1. reset

#### Details

NB:
  1. never bother about failures resulting from sending data pdus.

##### message send request received

if message send request is received for a remote endpoint, save any send request callback supplied.

Generate an id for the message.

Fragment the message into pdus into array of 1 header pdu and 0 or more data pdus. Ensure no pdu's data exceed preconfigured maximum limit.

Determine message destination id to use from known message destination ids for the remote endpoint, or use a random one if none exists. It is highly recommended to use previously obtained message source ids from an endpoint for use as the initial message destination id, in order to minimize the number of pdu exchanges per message.

Start the receive ack timer.
Send the first pdu of the message, and wait for the outcome. Ignore any send errors.

##### sent pdu callback invoked

if callback aftermath was not cancelled, then generate a random delay using preconfigured min/max backoff delays.

schedule repeat sending message of pdu at current index after delay.

##### retry backoff timer elapses

repeat sending message of pdu at current index, and waiting for outcome.

##### header ack received

assert that current index is 0, and ignore if otherwise.

assert that there is no error code, and abort send transfer if there is an error code.

The exception to aborting is if error code indicates 

  1. invalid message destination id. Save the message source id of the ack as the new message destination id to use for subsequent sends. Also it is highly recommended to save the message destination id for use with the remote endpoint, in order to speed up future message sends to that remote endpoint.
  2. data size too large. save the indicated pdu size received, and use it to refragment message for subsequent sends.

If there is no error code, then cancel any scheduled retry, and cancel send pdu callback aftermath. Reset the receive ack timer.

Move the current pdu index to the next one. If all pdus have been sent, then message send has been successful. Invoke the message send callback.

Else send the next pdu of the message, and wait for the outcome. Ignore any send errors.

##### data ack received

assert that current index is greater than 0, and ignore if otherwise.

assert that the sequence number of the ack equals the sequence number of the current pdu, and ignore if otherwise.

assert that there is no error code, and abort send transfer if there is an error code.

If there is no error code, then cancel any scheduled retry, and cancel send pdu callback aftermath. Reset the receive ack timer.

Move the current pdu index to the next one. If all pdus have been sent, then message send has been successful. Invoke the message send callback.

Else send the next pdu of the message, and wait for the outcome. Ignore any send errors.

##### receive ack timer elapses, reset

abort send transfer. Invoke message send callback.

Send back a pdu with same fields as current pdu but with empty data to trigger an early abort at receiver. Don't wait for outcome.
