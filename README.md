# PortableIPC Protocol

Defines and implements a network protocol suite at the session and application layer (of the OSI model) for inter-process communication (IPC) on same host machine (physical or virtual). Its primary purpose is to rival TCP as IPC mechanism of choice on a single host machine.

## Features

  * Based on UDP, and hence available across operating systems and programming platforms.
  * **Uses session ids unlike TCP, and so eliminates build up of TCP TIME_WAIT states (especially when HTTP connections are being set up and torn down frequently).**
  * Exposes configuration parameters such as PDU window size, idle/ack timeout, and maximum retry attempts on a per session basis to cater for different communication needs. In TCP such parameters can only be configured globally for all operating system connections.
  * **Makes streaming and duplex communication easier at application layer, by providing for idle timeout to be disabled without need for keep-alive packets, and also by leveraging UDP preservation of message boundaries.**
  * Takes advantage of same host constraint for increased performance by making it possible to increase MTU many times beyond Ethernet circa 1500 limit. This more than compensates for the potential drop in efficiency caused by protocol running in OS user mode (unlike TCP which runs in kernel mode).
  * Extensible for general network communication, by allowing for introduction of custom PDU types, congestion control, forward error correction and PDU expiration.


## Roadmap

 * C#.NET Core implementation is currently underway as the initial implementation.
 * Once initial implementation is done, the intention is to port to Java and NodeJS. To do this, initial implementation has been designed to work with blocking or non-block I/O (using abstraction of NodeJs promises) in single or multithreaded environments (using abstraction of Java Netty EventLoop). By such a design, porting to other programming environments should be straightforward.
