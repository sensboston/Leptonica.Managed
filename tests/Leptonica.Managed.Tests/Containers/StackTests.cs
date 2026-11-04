using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Containers;

public class StackTests
{
    private sealed class Item
    {
        public int Id;
    }

    [Fact]
    public void Remove_OnEmptyStack_ReturnsNull()
    {
        var s = L_Stack.Create(0);
        Assert.Equal(0, s.Count);
        Assert.Null(s.Remove());
    }

    [Fact]
    public void IsLifo_AndGrowsPastTheInitialArray()
    {
        var s = L_Stack.Create(0);   /* default size 20 */
        for (int i = 0; i < 1000; i++)
            s.Add(new Item { Id = i });
        Assert.Equal(1000, s.Count);
        for (int i = 999; i >= 0; i--)
            Assert.Equal(i, ((Item)s.Remove()!).Id);
        Assert.Equal(0, s.Count);
        Assert.Null(s.Remove());
    }

    [Fact]
    public void InterleavedAddAndRemove()
    {
        var s = L_Stack.Create(1);
        var a = new Item { Id = 1 };
        var b = new Item { Id = 2 };
        var c = new Item { Id = 3 };
        s.Add(a);
        s.Add(b);
        Assert.Same(b, s.Remove());
        s.Add(c);
        Assert.Equal(2, s.Count);
        Assert.Same(c, s.Remove());
        Assert.Same(a, s.Remove());
        Assert.Null(s.Remove());
    }

    [Fact]
    public void Create_ClampsTheInitialSize()
    {
        /* sizes <= 0 or above the maximum fall back to the default; the stack still works */
        foreach (int n in new[] { -5, 0, 1_000_000 }) {
            var s = L_Stack.Create(n);
            for (int i = 0; i < 100; i++)
                s.Add(new Item { Id = i });
            Assert.Equal(100, s.Count);
        }
    }

    [Fact]
    public void Add_NullItemThrows()
    {
        var s = L_Stack.Create(0);
        Assert.Throws<ArgumentException>(() => s.Add(null!));
    }

    [Fact]
    public void AuxStack_IsIndependent()
    {
        var s = L_Stack.Create(0);
        Assert.Null(s.AuxStack);
        s.AuxStack = L_Stack.Create(0);
        var item = new Item();
        s.AuxStack.Add(item);
        Assert.Equal(0, s.Count);
        Assert.Equal(1, s.AuxStack.Count);
        Assert.Same(item, s.AuxStack.Remove());
    }
}
