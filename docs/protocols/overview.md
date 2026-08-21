# Protocol Overview

Length-prefixed UTF-8 JSON-RPC over a local named pipe.

- Pipe name: `\\.\pipe\ParallelSeat-{userSid}`
- Max message: 1 MiB
- Handshake: protocol version + shared secret token
- Methods: seat.*, application.*, window.*, element.*, pointer.*, keyboard.*, action.*, safety.emergency_stop, diagnostics.*

See `src/ParallelSeat.Protocol` for contracts.
