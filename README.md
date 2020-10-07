# PortableIPC Protocol

Defines and implements a network protocol suited for inter-process communication (IPC) on same host machine (physical or virtual). It is intended to rival and replace TCP/HTTP for IPC between (web) applications within a single host machine.

## Features

  * Based on UDP, and hence available across operating systems and programming platforms.
  * **Uses session ids unlike TCP, and so eliminates TCP TIME_WAIT state build up as HTTP connections are set up and torn down.**
  * Exposes configuration parameters such as PDU window size, idle/ack timeout, and maximum retry attempts on a per session basis to cater for wider range of communication needs. In TCP such parameters can only be configured globally for all operating system connections, and are not available to be set by application code.
  * **Makes streaming or duplex communication easier at application layer, by providing for idle timeout to be disabled, and also by leveraging UDP preserving of message boundaries.**
  * Takes advantage of same host for increased performance by making it possible to increase MTU many times beyond Ethernet circa 1500 limit. This more than compensates for the drop in efficiency caused by protocol running in OS user mode (unlike TCP which runs in kernel mode).
  * Extensible for local area networks and networks in which store-and-forward network capability doesn't exist, or can be catered for by expiration mechanisms based on a network clock.


## Roadmap

 * C#.NET Core implementation is currently underway as the initial implementation.
 * Once initial implementation is done, the intention is to port to Java and NodeJS. To do this, initial implementation has been designed to work with blocking or non-block I/O (using abstraction of NodeJs promises) in single or multithreaded environments (using abstraction of Java Netty EventLoop). By such a design, porting to other programming environments should be straightforward.
