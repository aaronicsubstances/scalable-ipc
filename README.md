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
       - Endpoint owner id. Necessary for preventing repeated data processing during process restarts. Must be set differently at every start of the process which creates network endpoint.
       - Whether to use endpoint owner id as the constant message source id, or generate a new one per received message. Needed to handle any extent of delays and/or duplication of pdus.
       - Receive timeout of data PDUs. Necessary for cleaning up abandoned data transfers.
       - Time to wait before cleaning up received message ids.
       - Maximum transfer unit (MTU). Must be at least 512.
       - Minimum and maximum random backoff period between retries.
       - Receive timeout of acknowledgment PDUs. This becomes the time available for retries.


### PDU Structure

In the case of UDP, the datagram length indicates the length of the PDU.
In the case of TCP/TLS, the PDU must be wrapped in a TLV format.

message length, sequence number are signed 32-bit big endian integers. error code is signed 16-bit big endian integer.

with the exception of reserved, data payload, error code, no other field can be all zeros.

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
   - (payload, cannot be empty)

HEADER_ACK members
   - message source id - 16 bytes (uuid)
   - error code - 2 bytes

DATA members
   - message destination id - 16 bytes (uuid)
   - sequence number - 4 bytes
   - (payload, cannot be empty)

DATA_ACK members
   - message source id - 16 bytes (uuid)
   - sequence number - 4 bytes
   - error code - 2 bytes

### Receive Message Operation

  * receiver of message expects to get a header pdu, followed by 0 or more data pdus until message length indicated in header pdu is fully accumulated.
  * for receiver to process a header pdu, the following conditions must be met:
     * message id is not already being received.
     * message length is acceptable
     * there is enough space in receiver for message.
  * once these conditions are met, receiver proceeds to accept header pdu, and then waits to receive 0 or more data pdus.
  * for each header or data pdu received, receiver must respond with a header_ack or data_ack pdu. receiver should neither wait for or care about success of ack sending.
  * for each data pdu being waited for, receiver must set a timeout on it, in order to discard abandoned message transfers by sender.
  * the following conditions apply to processing of data and header pdus:
     * message id has not already been processed.
     * message destination id matches message source id at receiver.
     * there is enough space in receiver for pdu.
  * receiver must either use one message source id for all messages received in its lifetime, or dedicate a new one for each message.
  * In the case every message gets its own source id, then receiving an invalid message destination id in a header pdu should lead to an association of generated message source id with that message id (unless that association exists already). This association should be cleaned up after receive data timeout.
  * if conditions are not met during processing of a header or data pdu, receiver must respond with a header_ack or data_ack pdu with appropriate error_code set.
  * if header pdu is received again, while no data pdu has yet to be received, then receiver must respond by sending back the last header_ack sent.
  * after receiving header pdu, receiver must only accept data pdu with the expected sequence number, which starts from 1 and is incremented each time a data pdu is successfully received. Where a data pdu with the previous sequence number is received, receiver must respond by sending back the last data_ack sent.
  * once a message is successfully received in full, receiver must consider it processed and hold on to its id for some time (i.e. time to wait), before discarding it. By so doing repeated message processing will be avoided as long as duplicate pdus arrive at receiver before time to wait expires.
  * if a message is processed and awaiting discarding, and a header or data pdu is received whose sequence number matches the last sequence number (0 for header pdus) received in the processed message, then receiver must respond by sending back the last header_ack or data_ack sent.
  * if a timeout occurs while waiting for a data pdu, then receiver must consider the message processed and failed.
  * if a header or data pdu is received such that its addition will cause the message length to be exceeded, then receiver must pick the prefix of the pdu required to satisfy the message length.

### Send Message Operation

  * sender of message expects to receive acks for each pdu it sends, within a certain timeout per ack, in order for its data transfer to succeed.
  * Sender must be prepared to use different message destination ids for each message being sent.
  * the first pdu the sender must send should be a header pdu, with the length of the entire message set in it.
  * all subsequent ones must be data pdus with sequence numbers set to start from 1.
  * Any message destination id can be set for the first pdu, including previously received message destination ids for that endpoint.
  * size of each pdu must not exceed MTU of underlying transport, in order that pdu is not fragmented or rejected.
  * while sender is waiting for an ack, sender must continuously resend the current pdu awaiting the ack, with a random delay in between resends. Sender must wait for outcome of sending current pdu (but ignore awaited outcome whether it is success or error), before pausing to resend pdu again.
  * sender must discard acks which cannot be processed. e.g. because opcode or sequence number is unexpected.
  * once an ack with the expected opcode or sequence number is received, the sender must cancel (or consider as irrelevant the outcome of) the sending and resending of the current pdu being awaited, and proceed to send the next pdu or end the data transfer successfully.
  * if an ack indicates that the message destination id set is invalid, and the current pdu awaiting ack is a header pdu, and the message destination id being used is different, then sender must subsequenatly send pdus with the message source id of the ack as the message destination id to use.
  * if an ack indicates that receiver is running out of buffer space ... sender may choose to ignore error and keep trying to send current pdu.
  * in all other cases of receiving an error code, or receiving an ack timeout, sender must abort the data transfer.
