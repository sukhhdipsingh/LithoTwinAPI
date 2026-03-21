namespace LithoTwinAPI.EventSourcing;

// Started looking into Event Sourcing for the FSM transitions 
// but it's way too complicated for now. Sticking to EF Core.
public interface IEventStore
{
    // Task AppendEventAsync(string streamId, object @event);
    // Task<IEnumerable<object>> ReadEventsAsync(string streamId);
}
