using System.Collections.Generic;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Containers;

public class QueueTests
{
    private sealed class Item
    {
        public int Id;
    }

    [Fact]
    public void Remove_OnEmptyQueue_ReturnsNull()
    {
        var q = L_Queue<Item>.Create(0);
        Assert.Equal(0, q.Count);
        Assert.Null(q.Remove());
    }

    [Fact]
    public void IsFifo_AndGrowsPastTheInitialBuffer()
    {
        var q = L_Queue<Item>.Create(0);   /* default size 1024 */
        for (int i = 0; i < 5000; i++)
            q.Add(new Item { Id = i });
        Assert.Equal(5000, q.Count);
        for (int i = 0; i < 5000; i++)
            Assert.Equal(i, q.Remove()!.Id);
        Assert.Equal(0, q.Count);
        Assert.Null(q.Remove());
    }

    [Fact]
    public void InterleavedAddAndRemove_ShiftsTheItemsToTheFront()
    {
        /* A small buffer (20, the minimum) forces the shift-left path many times */
        var q = L_Queue<Item>.Create(20);
        var reference = new Queue<int>();
        int next = 0;
        var rng = new System.Random(3);
        for (int step = 0; step < 20000; step++) {
            if (rng.Next(3) != 0 || reference.Count == 0) {
                q.Add(new Item { Id = next });
                reference.Enqueue(next++);
            } else {
                Assert.Equal(reference.Dequeue(), q.Remove()!.Id);
            }
            Assert.Equal(reference.Count, q.Count);
        }
        while (reference.Count > 0)
            Assert.Equal(reference.Dequeue(), q.Remove()!.Id);
    }
}
