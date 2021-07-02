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

## Implementation Requirements

  * Use UDP as the standard transport for same host communications.
  * Use TCP/TLS as the standard transport for communications on the Internet.
  * Assume underlying transport is sufficiently secure and detects/prevents data corruption on its own. Protocol's part is to deal with dropped, delayed and/or duplicated PDUs.
  * Also assume underlying transport handles congestion on its own.
  * Simple error control by continuously retrying within a fixed time period with random backoff strategy. Underlying transport is at liberty to use more complicated backoff strategies behind the scenes.
  * Simple flow control with stop-and-wait. Underlying transport is at liberty to transparently use more complicated flow control mechanisms behind the scenes together with fragmenting PDUs.
  * Employ in-memory database to save already processed message ids. Necessary for preventing repeated data processing during lifetime of process.
  * Non-negotiable configuration parameters
       - Maximum transfer unit (MTU). Should be at least 512.
       - Maximum receivable message size. Should be at least 64 kilobytes (65536 bytes).
       - Minimum and maximum random backoff period between retries.
       - Receive timeout of acknowledgment PDUs. This becomes the time available for retries.
       - Receive timeout of data PDUs. Necessary for cleaning up abandoned data transfers.
       - Endpoint owner id. Necessary for preventing repeated data processing during process restarts. Should be set differently at every start of the process which creates network endpoint.
       - Time to wait before cleaning up processed data transfers.
       - Whether or not to set timestamps in PDUs.

## PDU Structure

In the case of UDP, the datagram length indicates the length of the PDU.
In the case of TCP/TLS, the PDU must be wrapped in a TLV format.

data transfer length, pdu length, and sequence numbers are signed 32-bit integers.
timestamp is 64-bit signed integer.

protocol version, opcode, error_code, message id and owner id cannot be all zeros.

all strings are utf8-encoded.

opcodes
   - header
   - header_ack
   - data
   - data_ack

Beginning members
   - opcode - 1 byte
   - protocol version - 1 byte
   - send time - 8 bytes (unix epoch in seconds)
   - reserved - 4 bytes

HEADER members
   - message id - 16 bytes (uuid)
   - message destination id - 16 bytes (uuid)
   - message length - 4 bytes

HEADER_ACK members
   - message id - 16 bytes
   - message source id - 16 bytes (uuid)
   - error code - 2 bytes

DATA members
   - message id - 16 bytes
   - message destination id - 16 bytes (uuid)
   - sequence number - 4 bytes
   - (payload, cannot be empty)

DATA_ACK members
   - message id - 16 bytes
   - message source id - 16 bytes (uuid)
   - sequence number - 4 bytes
   - error code - 2 bytes

## Receive Operation

Upon receipt of a PDU,

  * Validate opcode.
  * Validate protocol version.
  * Validate timestamp, unless timestamp is not a positive value. Only accept timestamps which differ from seconds of current unix epoch by less than time to wait.
  * Look in processed transfers for given message id and remote endpoint. If found, reply header/data pdus and ignore acks.
  * Remainder of processing depends on opcode.

For header pdu,
  * Look in incoming transfers for given message id and source endpoint. if incoming transfer is already present, then discard. unless expected seq number is 0, in which case reply with header_ack.
  * Validate total data transfer length. Must be positive. reply with message_too_large error pdu if needed.
  * Check if there is space to accept incoming transfer. reply with out_of_space error pdu if needed.
  * Validate message destination id against owner id. if invalid, reply with invalid_dest_id error pdu.
  * else no error so
  * respond with header_ack. ignore send error.
  * create incoming transfer for message id, and make expected seq number of subsequent data pdu 0.
  * set receive data timeout.

For data pdu,
  * Look in incoming transfers for given message id and source endpoint.
  * validate seq number. For invalid case where seq number is the previous one, end by replying with
    data_ack with previous seq number.
  * respond with overflow error pdu if addition of data pdu will cause already accumulated data to overflow.
  * else no error so
  * cancel receive data timeout.
  * respond with data_ack. ignore send error.
  * Add to already accumulated data for incoming transfer.
  * If total expected is received, remove from incoming transfers, add message id to processed transfers, and schedule for removal after time to wait. Notify application receipt handler with message id and total bytes.
  * Else not done, so increase expected seq number, and set receive data timeout.
  
For header_ack and data_ack pdus,
  * Look in outgoing transfers for given message id and destination endpoint.
  * Validate seq number.
  * else no error so
  * cancel receive ack timeout.
  * notify outgoing transfer handler.

Upon receive data timeout,
  * Mark incoming transfer as failed, unless it has succeeded already.
  * don't respond with an ack.
  * remove from incoming transfers, and add message id to processed transfers. Schedule for removal after time to wait.

## Send Operation

  * receive message to send, destination endpoint, and a callback which will be notified of the outcome.
  * assign a random uuid as message id to outgoing transfer and return id to application.
  * create outgoing transfer handler.

In outgoing transfer handler,
  * split message into pdus using MTU size.
  * set message destination id from message source id of last received ack. If the very first time, use any id.
  * to handle retries, have a dedicated retry handler per pdu, and set receive ack timeout before starting each.
  * if ack is received, cancel retry handler.
  * if ack timeout fires or an error_code is received, then it almost certainly means outgoing transfer has failed. Cancel retry handler, and notify send operation callback. remove from outgoing transfers.
  * Except for transient error cases like invalid_dest_id, and even that for header ack case. in that case set message destination id to use subsequently.
  * only if ack doesn't indicate error, should ack timeout be cancelled.
  * create next retry handler for next pdu.
  * if all pdus are done sending, remove from outgoing transfers, add message id to processed transfers, and schedule for removal after time to wait. Notify send operation callback.

In retry handler, given a pdu, repeat the following until cancellation.
  * set timestamp if configuration permits, and send pdu asynchronously.
  * wait for success or error. ignore send errors.
  * generate backoff time randomly
  * pause

Upon receive ack timeout,
  * remove from outgoing transfers unless it has succeeded already
  * Cancel retry handler
  * notify send operation callback with send timeout error.
