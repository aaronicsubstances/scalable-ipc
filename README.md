# PortableIPC Protocol

Defines and implements a network protocol suite to rival TCP/HTTP as IPC mechanism of choice on a **single host machine** and on "interconnects" (internal networks). *The initial motivation for targetting interconnects comes from IPC between microservice-based web applications.*

## Features

  * Based on UDP, and hence available across operating systems and programming platforms unlike Unix domain sockets and Windows named pipes.
  * **Uses session ids unlike TCP, and so enables multiplexing unlike in HTTP, eliminates head-of-line blocking in TCP, and eliminates build up of HTTP/TCP TIME_WAIT states (when HTTP connections are not being reused, which is the usual case).**
  * Exposes configuration parameters such as maximum window size, idle/ack timeout, and maximum retry attempts on a per application basis to cater for different communication needs. In TCP such parameters can only be configured globally for all operating system connections.
  * **Makes streaming and duplex communication easier at application layer, by providing for idle timeout to be disabled without need for keep-alive packets, and also by leveraging UDP preservation of message boundaries.**
  * Optimized towards communications within single machine host, or within LAN, or between similar networks. Efficiency of communications between dissimilar networks is of lower priority.
  * Extensible for communication on internal networks (e.g. for use by microservice-based web applications), by allowing for introduction of congestion control, transport security (DTLS), forward error correction, and whatever is possible with custom PDU types and options.


## Roadmap

 * C#.NET Core implementation is currently underway as the initial implementation.
 * Once initial implementation is done, the intention is to port to Java and NodeJS. To do this, initial implementation has been designed to work with non-blocking I/O (using abstraction of NodeJs promise and C#.NET Core TaskCompletionSource) in single or multithreaded environments (using abstraction of Java Netty EventLoop). By such a design, porting to other programming environments should be straightforward.
