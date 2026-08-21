using ParallelSeat.Core;
using Xunit;

namespace ParallelSeat.SoakTests;

public class SeatLifecycleSoakTests
{
    [Fact]
    public async Task Create_Attach_Destroy_Loop_DoesNotLeakSeats()
    {
        var overlay = new CountingOverlay();
        var manager = new SeatManager(overlay);
        for (var i = 0; i < 50; i++)
        {
            var id = $"soak-{i}";
            var seat = manager.Create(id);
            seat.BeginAttach();
            seat.Activate(new ApplicationInstanceId(i + 1, DateTime.UtcNow, "soak.exe", [i + 1]), [i + 1], [new IntPtr(i + 1)]);
            overlay.Show(id, new ScreenPoint(i, i), null);
            manager.Destroy(id);
        }

        Assert.Empty(manager.List());
        Assert.Equal(0, overlay.LiveCount);
        await Task.CompletedTask;
    }

    private sealed class CountingOverlay : IOverlayController
    {
        private readonly HashSet<string> _live = new(StringComparer.Ordinal);
        public int LiveCount { get { lock (_live) return _live.Count; } }

        public void Show(string seatId, ScreenPoint logicalCursor, BoundingRect? targetOutline, string label = "AI")
        {
            lock (_live) _live.Add(seatId);
        }

        public void Update(string seatId, ScreenPoint logicalCursor, BoundingRect? targetOutline, string feedback = "") { }

        public void Hide(string seatId)
        {
            lock (_live) _live.Remove(seatId);
        }

        public void HideAll()
        {
            lock (_live) _live.Clear();
        }
    }
}
