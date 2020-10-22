# PortableIPC Protocol

Defines and implements a network protocol suite to replace HTTP as IPC mechanism of choice on a **single host machine** and on "interconnects" (internal networks). *The initial motivation for this protocol is for IPC between microservice-based web applications.*

## Features

  * Based on TCP/TLS and UDP.
  * Enables multiplexing by using application layer session ids.
  * Eliminates unbounded build up of TIME_WAIT states in underlying TCP of HTTP, especially when HTTP connections are not being reused, which is the usual case.
  * Makes streaming and duplex communication easier by working with messages (like UDP datagrams) and presenting option to disable idle timeout per session.
  * Optimized for communications within single machine host by using UDP. E.g. eliminates need for TLS, head-of-line blocking and TIME_WAIT state in TCP. Also can enable faster start up with larger MTU and window size, and quicker session shutdown with lower values for ack timeout and max retry count.

## Roadmap

 * C#.NET Core implementation is currently underway as the initial implementation.
 * Once initial implementation is done, the intention is to port to Java and NodeJS. To do this, initial implementation has been designed to work with non-blocking I/O (using abstraction of NodeJs promise and C#.NET Core TaskCompletionSource) in single or multithreaded environments (using abstraction of Java Netty EventLoop). By such a design, porting to other programming environments should be straightforward.
