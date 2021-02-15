# ScalableIPC Protocol

Defines and implements an application layer network protocol to serve as 

   1. OS-neutral IPC mechanism of choice on a single host machine (localhost)
   2. Protocol of choice for use on internal/backend networks of applications running on the Internet.
   3. Common interface to pluggable underlying networks for HTTP, in order to dissociate HTTP from TCP, and leverage any alternative underlying network available which may be more efficient depending on the context (e.g. on a single host machine).
   
The initial motivation for this protocol came from deliberations on IPC efficiency between microservice-based web applications.

## Features

  * Uses multiplexed TCP/TLS by default. In general however, underlying network is an abstraction for the protocol, and hence it can be used on top of any network, even over unreliable ones, depending on the context.
  * Enables multiplexing regardless of underlying network by using application layer session ids.
  * Places upper bound on TCP TIME_WAIT state count when using multiplexed TCP. In contrast when non-persistent HTTP connections are being used, TIME_WAIT states just keep increasing and hogging ports per TCP design, for time period of MSL value of 1 minute or more.
  * Exposes configuration parameters such as maximum window size, MTU, idle/ack timeout, and maximum retry attempts on a per application basis, and thus makes the protocol adaptable to a wide range of networking needs. When using TCP directly however, such parameters can only be configured globally for all operating system connections.
  * Makes streaming and duplex communication easier at application layer, by

     * enabling idle timeout to be applied or disabled per session.
     * treating all errors as transient, so that sessions persist in spite of errors. Sessions also persist without the need for keep-alive packets.
     * preserving message boundaries like in UDP.

  * Optimized for networking within single host machine by using faster IPC mechanisms where available, such as UDP, Unix domain sockets and Windows named pipes. *By such a design, the protocol can be set up once for networking on single host machine, and will not have to be swapped out for interhost network communications.* Hence the name **ScalableIPC**, i.e. it can scale *down* from global internetworking to localhost internetworking; and also scale *up* from localhost to global.

  * Designed to make maximum utilisation of "long fat networks", ie internal networks with large bandwidth-delay products.

## Protocol State Machine

![protocol state machine](psm.png)

**NB:**

   - calling ISessionHandler.CompleteInit launches the opening state.

   - in opening state only data pdus are processed; all others are ignored. Time spent in this state determines open timeout.

   - in opened state,

      - data pdus are processed normally, and the activity here determines idle timeout.

      - enquire link pdus are sent out periodically without waiting for reply or even network send aftermath.

      - enquire link pdus are processed by sending back enquire link acks.

   - in closing state, close pdu is sent before transitioning to Awaiting_Disposal state. If gracious close was requested, then attempt is made to wait for ack pdu. Else for force close, only network send aftermath is awaited.

   - beyond opened state (i.e. when session is disposing), data and enquire link pdus are ignored.

   - beyond closing state, close pdus are ignored.

## Data Exchange Protocol

   1. Aim of having an opening state is to implement guarantee for network api implementations that: *while a session is in this state, its remote peer can be ignored at any time without fear of remote processing side effects.*

   1. During opening state (and in the absence of *skip_data_exchange_restrictions* option), the following data exchange restrictions apply:

      1. A session cannot send and receive data at the same time.

      2. A window group to be sent and received must consist of 2 or more windows.

   1. The prescence of a skip_data_exchange_restrictions option during sending or receiving, immediately transitions the opening state to the opened state prior to processing, so that none of the opening state restrictions can be applied.

   1. During receive data and presuming a current window buffer (array of pdus) and a window group buffer (array of windows),

      1. use *first_in_window_group* option to clear window group buffer. use pdu with different window id to clear current window buffer. 

      1. always reply with ack if possible. An ack reply means that all seq_nrs not exceeding that of the received pdu have been received. Thus cannot reply with ack if seq_nr = 0 has not been received.

      1. use *last_in_window* option to indicate that a seq_nr ends a data window exchange. Receipt of a last_in_window pdu clears any existing position with a last_in_window (so that seq_nr must be sent again), and all pdus after received pdu's seq_nr.
 
      1. use *last_in_window_group* option to stop waiting for additions to window group buffer and process it for application layer. last_in_window_group option only applies if last_in_window option is set.

      1. if current window buffer fills up before receipt of a last_in_window option (e.g. because receiver's buffer size is smaller than that of sender), set *is_window_full* option to true on ack reply. Also use *max_window_size* option in ack reply to communicate buffer size for use by sender in the future. Current window buffer is emptied into window group buffer if filled up for any reason.

      1.  any processing error of a window or window group should be communicated in ack reply with the *error_code* option indicating kind of processing error. Some processing errors are (a) window group option decoding error (b) window group exceeding 65500 max byte limit (c) receiving window group with only 1 window in opening state.

      1. if a pdu is deemed valid, its window id becomes the minimum window id which will be accepted from remote peers. In any case once a window is processed whether successfully or not, its window id will not be accepted again.

      1. a pdu is invalid if its window id or seq_nr is negative. It is also invalid if its window id is less than the current minimum, or if its window id exceeds the current minimum by a difference of more than 100. If seq_nr is too large as indicated by current window buffer size, again pdu is invalid.
 
      1.  The very first window id must be 100 or less, and the maximum possible value is 9E15 (chosen to be representable exactly as a double precision floating point number). After that limit, window ids wrap around to 100 or less.

      1. window id validations apply across pdu op code variations and session state changes.

      1. if at any time an invalid window id is received, but happens to be equal to the last processed window id, *then the same ack which was sent as reply for that window must be sent again, regardless of the state the session is currently in.*

   1. With regards to sending data, protocol presumes the sending of bytes of data and associated attributes - key value pairs. Protocol also presumes bytes of data is divided into window groups, and each window group is in turn divided into prospective windows. A prospective window is an array of pdus which may or may not be received fully at remote peer in 1 window. A current window group and a current prospective window exist as the focus of send operation at any given time.

      1.  Only 1 send operation can be ongoing at a time across data exchanges and gracious close requests. "fire and forget" sends however can always be made.

      1. Between peers, max window size can vary and hence do not have to be synchronized. In general within a peer though, send window size equals receive window size.

      1. Pdus must not exceed MTU set by network implementation.

      1. A window group must not exceed 65500 byte limit (chosen to be close to theoretical max UDP payload size).

      1. Attributes will be attached as pdu options to each window group being sent.

      1. Some send outcomes are (a) network send error (b) ack error code (c) eventual ack timeout (d) eventual success

      1. use ack timeout and retry count per window. max retry count determines eventual ack timeout.

      1. retry sending one at a time (i.e. send one pdu, wait for ack with is_window_full option before sending next) from the beginning of the current prospective window if ack timeout occurs. Do not use received acks to assume certain seq_nrs have been received in between retries. Network implementations can depend on this "retry from beginning on timeout" feature.

      1. However, once the very first pdu in prospective window has been sent during retrying, definitely use received acks to determine what has been received, and skip ahead in current prospective window to what needs to be sent next.

      1. If starting a prospective window send for the first time, then send all in succession before waiting for ack with is_window_full option for last pdu in window (rather than one at a time). Reject acks received for pdus which have not been sent or have already been processed. However, if an ack is received while waiting for a network send aftermath for a pdu, then use ack, disregard the network send outcome, and restart sending from where the ack's seq_nr indicates. Network implementations can depend on this "ack can be sent before network send response" feature.

      1. if is_window_full option is received in an ack pdu, use the ack's seq_nr to partition current prospective window into two. The first part joins the actual sent windows for the current window group being sent, and the second part takes over as the new current prospective window. Assign a new window id to it and send it as a new window with no retry history. If the current prospective window becomes empty, then the original prospective window is deemed successfully sent through one or more actual sent windows, and the next prospective window from the current or next window group can be sent. If there is no longer any prospective window to send, then send operation ends with an eventual success.

## Roadmap

 * C#.NET Core implementation is currently underway as the initial implementation.
 * Once initial implementation is done, the intention is to port to Java and NodeJS. To do this, initial implementation has been designed to work with blocking or non-blocking I/O (using abstraction of NodeJs promise and C#.NET Core TaskCompletionSource), in single or multithreaded environments (using abstraction of NodeJS event loop). By such a design, porting to other programming environments should be straightforward.
