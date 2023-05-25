using System;
using System.Collections.Generic;
using System.Text;
using XLua;

namespace GenericUndoRedo
{
    /// <summary>
    /// Stack with capacity, bottom items beyond the capacity are discarded.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [Serializable]
    [LuaCallCSharp]
    public class RoundStack<T>
    {
        private T[] items; // items.Length is Capacity + 1

        // top == bottom ==> full
        private int top = 1;
        private int bottom = 0;

        /// <summary>
        /// Gets if the <see cref="RoundStack&lt;T&gt;"/> is full.
        /// </summary>
        public bool IsFull
        {
            get
            {
                return top == bottom;
            }
        }

        /// <summary>
        /// Gets the number of elements contained in the <see cref="RoundStack&lt;T&gt;"/>.
        /// </summary>
        public int Count
        {
            get
            {
                int count = top - bottom - 1;
                if (count < 0)
                    count += items.Length;
                return count;
            }
        }

        /// <summary>
        /// Gets the capacity of the <see cref="RoundStack&lt;T&gt;"/>.
        /// </summary>
        public int Capacity
        {
            get
            {
                return items.Length - 1;
            }
        }

        /// <summary>
        /// Creates <see cref="RoundStack&lt;T&gt;"/> with given capacity
        /// </summary>
        /// <param name="capacity"></param>
        public RoundStack(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException("Capacity need to be at least 1");
            items = new T[capacity + 1];
        }

        /// <summary>
        /// Removes and returns the object at the top of the <see cref="RoundStack&lt;T&gt;"/>.
        /// </summary>
        /// <returns></returns>
        public T Pop()
        {
            if (Count > 0)
            {
                T removed = items[top];
                items[top--] = default(T);
                if (top < 0)
                    top += items.Length;
                return removed;
            }
            else
                throw new InvalidOperationException("Cannot pop from emtpy stack");
        }

        /// <summary>
        /// Inserts an object at the top of the <see cref="RoundStack&lt;T&gt;"/>.
        /// </summary>
        /// <param name="item"></param>
        public void Push(T item)
        {
            if (IsFull)
            {
                bottom++;
                if (bottom >= items.Length)
                    bottom -= items.Length;
            }
            if (++top >= items.Length)
                top -= items.Length;
            items[top] = item;
        }

        /// <summary>
        /// Returns the object at the top of the <see cref="RoundStack&lt;T&gt;"/> without removing it.
        /// </summary>
        public T Peek()
        {
            return items[top];
        }

        /// <summary>
        /// Removes all the objects from the <see cref="RoundStack&lt;T&gt;"/>.
        /// </summary>
        public void Clear()
        {
            if (Count > 0)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    items[i] = default(T);
                }
                top = 1;
                bottom = 0;
            }
        }

    }
}