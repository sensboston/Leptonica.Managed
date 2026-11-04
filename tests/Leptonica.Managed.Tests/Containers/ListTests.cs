using System;
using System.Collections.Generic;
using Leptonica;

namespace Leptonica.Managed.Tests.ContainersTests;

public class ListTests
{
    private sealed class Item
    {
        public readonly int Id;
        public Item(int id) { Id = id; }
    }

    private static List<int> Forward(L_Dllist<Item>? head)
    {
        var list = new List<int>();
        for (var c = head; c != null; c = c.Next)
            list.Add(c.Data.Id);
        return list;
    }

    private static List<int> Backward(L_Dllist<Item> head)
    {
        var list = new List<int>();
        for (var c = L_Dllist<Item>.FindTail(head); c != null; c = c.Prev)
            list.Add(c.Data.Id);
        return list;
    }

    private static L_Dllist<Item>? Build(params int[] ids)
    {
        L_Dllist<Item>? head = null, tail = null;
        foreach (int id in ids)
            L_Dllist<Item>.AddToTail(ref head, ref tail, new Item(id));
        return head;
    }

    [Fact]
    public void AddToHead_BuildsReversedList()
    {
        L_Dllist<Item>? head = null;
        for (int i = 1; i <= 3; i++)
            L_Dllist<Item>.AddToHead(ref head, new Item(i));
        Assert.Equal(new[] { 3, 2, 1 }, Forward(head));
        Assert.Equal(new[] { 1, 2, 3 }, Backward(head!));
        Assert.Null(head!.Prev);
    }

    [Fact]
    public void AddToTail_UpdatesTailAndFindsItWhenNull()
    {
        L_Dllist<Item>? head = null, tail = null;
        L_Dllist<Item>.AddToTail(ref head, ref tail, new Item(1));
        L_Dllist<Item>.AddToTail(ref head, ref tail, new Item(2));
        Assert.Same(tail, L_Dllist<Item>.FindTail(head!));
        Assert.Equal(2, tail!.Data.Id);
        // tail forgotten: it is searched for
        tail = null;
        L_Dllist<Item>.AddToTail(ref head, ref tail, new Item(3));
        Assert.Equal(new[] { 1, 2, 3 }, Forward(head));
        Assert.Equal(3, tail!.Data.Id);
    }

    [Fact]
    public void InsertBefore_AllPositions()
    {
        L_Dllist<Item>? head = null;
        L_Dllist<Item>.InsertBefore(ref head, null, new Item(2));
        L_Dllist<Item>.InsertBefore(ref head, head, new Item(1));          // before head
        L_Dllist<Item>.InsertBefore(ref head, head!.Next, new Item(9));    // in the middle
        Assert.Equal(new[] { 1, 9, 2 }, Forward(head));
        Assert.Equal(new[] { 2, 9, 1 }, Backward(head!));
        Assert.Throws<ArgumentException>(() => L_Dllist<Item>.InsertBefore(ref head, null, new Item(5)));
        L_Dllist<Item>? empty = null;
        Assert.Throws<ArgumentException>(() => L_Dllist<Item>.InsertBefore(ref empty, head, new Item(5)));
    }

    [Fact]
    public void InsertAfter_AllPositions()
    {
        L_Dllist<Item>? head = null;
        L_Dllist<Item>.InsertAfter(ref head, null, new Item(1));
        L_Dllist<Item>.InsertAfter(ref head, head, new Item(3));           // after last
        L_Dllist<Item>.InsertAfter(ref head, head, new Item(2));           // in the middle
        Assert.Equal(new[] { 1, 2, 3 }, Forward(head));
        Assert.Equal(new[] { 3, 2, 1 }, Backward(head!));
        Assert.Throws<ArgumentException>(() => L_Dllist<Item>.InsertAfter(ref head, null, new Item(5)));
    }

    [Fact]
    public void RemoveElement_FirstMiddleLastOnly()
    {
        var head = Build(1, 2, 3, 4);
        var second = head!.Next!;
        Assert.Equal(2, L_Dllist<Item>.RemoveElement(ref head, second).Id);
        Assert.Equal(new[] { 1, 3, 4 }, Forward(head));
        Assert.Equal(1, L_Dllist<Item>.RemoveElement(ref head, head!).Id);
        Assert.Equal(new[] { 3, 4 }, Forward(head));
        Assert.Null(head!.Prev);
        Assert.Equal(4, L_Dllist<Item>.RemoveElement(ref head, head!.Next!).Id);
        Assert.Equal(new[] { 3 }, Forward(head));
        Assert.Equal(3, L_Dllist<Item>.RemoveElement(ref head, head!).Id);
        Assert.Null(head);
        L_Dllist<Item>? none = null;
        Assert.Throws<ArgumentException>(() => L_Dllist<Item>.RemoveElement(ref none, Build(1)!));
        var other = Build(7);
        var h1 = Build(1);
        Assert.Throws<ArgumentException>(() => L_Dllist<Item>.RemoveElement(ref h1, other!));
    }

    [Fact]
    public void RemoveFromHead_And_Tail()
    {
        var head = Build(1, 2, 3);
        L_Dllist<Item>? tail = null;
        Assert.Equal(3, L_Dllist<Item>.RemoveFromTail(ref head, ref tail).Id);
        Assert.Equal(2, tail!.Data.Id);
        Assert.Equal(1, L_Dllist<Item>.RemoveFromHead(ref head).Id);
        Assert.Equal(new[] { 2 }, Forward(head));
        Assert.Equal(2, L_Dllist<Item>.RemoveFromTail(ref head, ref tail).Id);
        Assert.Null(head);
        Assert.Null(tail);
        Assert.Throws<ArgumentException>(() => L_Dllist<Item>.RemoveFromHead(ref head));
        Assert.Throws<ArgumentException>(() => L_Dllist<Item>.RemoveFromTail(ref head, ref tail));
    }

    [Fact]
    public void FindElement_UsesReferenceEquality()
    {
        var a = new Item(1);
        var b = new Item(1);
        L_Dllist<Item>? head = null, tail = null;
        L_Dllist<Item>.AddToTail(ref head, ref tail, a);
        L_Dllist<Item>.AddToTail(ref head, ref tail, b);
        Assert.Same(tail, L_Dllist<Item>.FindElement(head!, b));
        Assert.Same(head, L_Dllist<Item>.FindElement(head!, a));
        Assert.Null(L_Dllist<Item>.FindElement(head!, new Item(1)));
    }

    [Fact]
    public void GetCount_Reverse_Join()
    {
        var head = Build(1, 2, 3, 4);
        Assert.Equal(4, L_Dllist<Item>.GetCount(head!));
        L_Dllist<Item>.Reverse(ref head);
        Assert.Equal(new[] { 4, 3, 2, 1 }, Forward(head));
        Assert.Equal(new[] { 1, 2, 3, 4 }, Backward(head!));

        var other = Build(8, 9);
        L_Dllist<Item>.Join(ref head, ref other);
        Assert.Equal(new[] { 4, 3, 2, 1, 8, 9 }, Forward(head));
        Assert.Equal(new[] { 9, 8, 1, 2, 3, 4 }, Backward(head!));
        Assert.Null(other);

        L_Dllist<Item>? none = null;
        L_Dllist<Item>.Join(ref head, ref none);
        Assert.Equal(6, L_Dllist<Item>.GetCount(head!));
        L_Dllist<Item>? empty1 = null;
        var h2 = Build(5, 6);
        L_Dllist<Item>.Join(ref empty1, ref h2);
        Assert.Equal(new[] { 5, 6 }, Forward(empty1));
        Assert.Null(h2);
        L_Dllist<Item>? nothing = null;
        Assert.Throws<ArgumentException>(() => L_Dllist<Item>.Reverse(ref nothing));
    }
}
