# PortableIPC Protocol

Defines and implements a network protocol suite to replace HTTP as IPC mechanism of choice on a **single host machine** and on "interconnects" (internal networks). *The initial motivation for this protocol is for IPC between microservice-based web applications.*

## Features

  * Based on UDP and TCP/TLS for IPC on single host machine and the Internet respectively.
  * Enables multiplexing by using application layer session ids.
  * Places upper bound on HTTP/TCP TIME_WAIT state count. In contrast when HTTP connections are not being reused (which is the usual case), TIME_WAIT states just keep piling up and hogging ports per TCP design, for time period of MSL value of 1 minute or more.
  * Exposes configuration parameters such as maximum window size, MTU, idle/ack timeout, and maximum retry attempts on a per application basis, and thus makes the protocol adaptable to a wide range of networking needs. In HTTP/TCP such parameters can only be configured globally for all operating system connections.
  * Makes streaming and duplex communication easier at application layer, by providing for idle timeout to be disabled without need for keep-alive packets, and also by leveraging UDP preservation of message boundaries.
  * Optimized for communications within single host machine, e.g. use of UDP eliminates head-of-line blocking issue in TCP.
  * Extensible for use on the Internet as a TCP alternative by using UDP and allowing for introduction of congestion control, transport security (e.g. DTLS), forward error correction, and whatever is possible with custom PDU types and options.

## Roadmap

 * C#.NET Core implementation is currently underway as the initial implementation.
 * Once initial implementation is done, the intention is to port to Java and NodeJS. To do this, initial implementation has been designed to work with non-blocking I/O (using abstraction of NodeJs promise and C#.NET Core TaskCompletionSource) in single or multithreaded environments (using abstraction of Java Netty EventLoop). By such a design, porting to other programming environments should be straightforward.
