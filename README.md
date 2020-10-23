# PortableIPC Protocol

Defines and implements a network protocol suite to replace HTTP as IPC mechanism of choice on a **single host machine** (localhost) and on "interconnects" (internal networks). *The initial motivation for this protocol is for IPC between microservice-based web applications.*

## Features

  * Based on UDP and TCP/TLS for IPC on single host machine and the Internet respectively.
  * Enables multiplexing by using application layer session ids.
  * Places upper bound on TCP TIME_WAIT state count. In contrast when HTTP connections are not being reused (which is the usual case), TIME_WAIT states just keep increasing and hogging ports per TCP design, for time period of MSL value of 1 minute or more.
  * Exposes configuration parameters such as maximum window size, MTU, idle/ack timeout, and maximum retry attempts on a per application basis, and thus makes the protocol adaptable to a wide range of networking needs. In HTTP/TCP such parameters can only be configured globally for all operating system connections.
  * Makes streaming and duplex communication easier at application layer, by

     * enabling idle timeout to be applied end-to-end.
     * disabling idle timeout per session without need for keep-alive packets.
     * preserving transport message boundaries like in UDP.

  * Optimized for networking within single host machine, e.g. use of UDP eliminates head-of-line blocking issue in TCP.
  * Extensible for use on the Internet without TCP by using UDP and allowing for congestion control, transport security (e.g. DTLS), forward error correction, and whatever is possible with custom PDU types, options and session state handlers.

## Roadmap

 * C#.NET Core implementation is currently underway as the initial implementation.
 * Once initial implementation is done, the intention is to port to Java and NodeJS. To do this, initial implementation has been designed to work with blocking or non-blocking I/O (using abstraction of NodeJs promise and C#.NET Core TaskCompletionSource), in single or multithreaded environments (using abstraction of Java Netty EventLoop). By such a design, porting to other programming environments should be straightforward.
