# PortableIPC Protocol

Defines and implements a network protocol suite to rival TCP/HTTP as IPC mechanism of choice on a **single host machine** and on "interconnects" (internal networks). *The initial motivation for including interconnects comes from IPC between microservice-based web applications.*

## Features

  * Based on UDP, and hence available across operating systems and programming platforms.
  * **Uses session ids unlike TCP, and so eliminates build up of TCP TIME_WAIT states (especially when HTTP connections are being set up and torn down frequently).**
  * Exposes configuration parameters such as window buffer size, idle/ack timeout, and maximum retry attempts on a per application basis to cater for different communication needs. In TCP such parameters can only be configured globally for all operating system connections.
  * **Makes streaming and duplex communication easier at application layer, by providing for idle timeout to be disabled without need for keep-alive packets, and also by leveraging UDP preservation of message boundaries.**
  * Takes advantage of same host constraint for increased performance by making it possible to increase MTU many times beyond Ethernet circa 1500 limit. This more than compensates for the potential drop in efficiency caused by protocol running in OS user mode (unlike TCP which runs in kernel mode).
  * Extensible for communication on internal high bandwidth networks (e.g. for use by microservice-based web applications), by allowing for introduction of congestion control, transport security, forward error correction and custom PDU types.


## Roadmap

 * C#.NET Core implementation is currently underway as the initial implementation.
 * Once initial implementation is done, the intention is to port to Java and NodeJS. To do this, initial implementation has been designed to work with non-blocking I/O (using abstraction of NodeJs promise and C#.NET Core TaskCompletionSource) in single or multithreaded environments (using abstraction of Java Netty EventLoop). By such a design, porting to other programming environments should be straightforward.
