# ScalableIPC Protocol

Defines and implements an application layer network protocol to serve as 

   1. OS-neutral IPC mechanism of choice on a single host machine (localhost).
   2. Application layer protocol which is more efficient than HTTP for "interconnects" (i.e. internal networks).
   
The initial motivation for this protocol came from deliberations on IPC efficiency between microservice-based web applications.

## Features

  * Based on TCP/TLS by default. In general however, underlying network is an abstraction for the protocol, and hence it can be used on top of any transport layer protocol, even over unreliable ones.
  * Enables multiplexing by using application layer session ids.
  * Places upper bound on TCP TIME_WAIT state count. In contrast when HTTP connections are not being reused (which is the usual case), TIME_WAIT states just keep increasing and hogging ports per TCP design, for time period of MSL value of 1 minute or more.
  * Exposes configuration parameters such as maximum window size, MTU, idle/ack timeout, and maximum retry attempts on a per application basis, and thus makes the protocol adaptable to a wide range of networking needs. In HTTP/TCP such parameters can only be configured globally for all operating system connections.
  * Makes streaming and duplex communication easier at application layer, by

     * enabling idle timeout to be applied end-to-end.
     * disabling idle timeout per session without need for keep-alive packets.
     * preserving transport message boundaries like in UDP.

  * Optimized for networking within single host machine by using UDP. *By such a design, the protocol can be set up once for networking on single host machine, and will not have to be swapped out for interhost network communications.* Hence the name **ScalableIPC**, i.e. it can scale from single host networking to interhost networking.
  * Extensible for use as a standalone transport layer protocol, by using UDP and allowing for congestion control, transport security (e.g. DTLS), forward error correction, and whatever is possible with custom PDU types, options and session state handlers.

## Roadmap

 * C#.NET Core implementation is currently underway as the initial implementation.
 * Once initial implementation is done, the intention is to port to Java and NodeJS. To do this, initial implementation has been designed to work with blocking or non-blocking I/O (using abstraction of NodeJs promise and C#.NET Core TaskCompletionSource), in single or multithreaded environments (using abstraction of NodeJS event loop). By such a design, porting to other programming environments should be straightforward.
