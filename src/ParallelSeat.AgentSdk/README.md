# ParallelSeat Python Agent SDK

Async client for the ParallelSeat host named-pipe JSON-RPC protocol.

```python
async with ParallelSeatClient(token="...") as client:
    async with client.create_seat(seat_id="ai-primary", strict_no_focus_steal=True) as seat:
        await seat.attach_to_application(process_id=1234)
        target = await seat.find_element(automation_id="AiValueField")
        await target.set_value("60")
```

This package does not perform GUI automation itself; all actions are mediated by the host safety policy.
