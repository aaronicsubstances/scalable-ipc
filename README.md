# PortableIPC Protocol

Defines and implements a network protocol suite to replace HTTP as IPC mechanism of choice on a **single host machine** and on "interconnects" (internal networks). *The initial motivation for this protocol is for IPC between microservice-based web applications.*

## Features

  * Based on UDP for IPC on single host machine, and TCP/TLS for use on the Internet.
  * Enables multiplexing by using application layer session ids.
  * Eliminates unbounded build up of TCP TIME_WAIT states in HTTP, especially when HTTP connections are not being reused, which is the usual case.
  * Makes streaming and duplex communication easier at application layer, by providing for idle timeout to be disabled without need for keep-alive packets, and also by leveraging UDP preservation of message boundaries.
  * Optimized for communications within single host machine.
  * Extensible for use on the Internet as a TCP alternative by using UDP and allowing for introduction of congestion control, transport security (DTLS), forward error correction, and whatever is possible with custom PDU types and options. Also exposes configuration parameters such as maximum window size, idle/ack timeout, and maximum retry attempts on a per application basis to cater for different communication needs. In TCP such parameters can only be configured globally for all operating system connections.

## Roadmap

 * C#.NET Core implementation is currently underway as the initial implementation.
 * Once initial implementation is done, the intention is to port to Java and NodeJS. To do this, initial implementation has been designed to work with non-blocking I/O (using abstraction of NodeJs promise and C#.NET Core TaskCompletionSource) in single or multithreaded environments (using abstraction of Java Netty EventLoop). By such a design, porting to other programming environments should be straightforward.
