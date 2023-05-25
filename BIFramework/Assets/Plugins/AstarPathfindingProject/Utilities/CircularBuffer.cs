using System.Collections.Generic;
using Pathfinding.Util;

namespace Pathfinding.Util {
	/// <summary>Implements an efficient circular buffer that can be appended to in both directions</summary>
	public struct CircularBuffer<T> {
		T[] data;
		int head;
		int length;

		public int Length => length;
		public int AbsoluteStartIndex => head;
		public int AbsoluteEndIndex => head + length - 1;

		public T First => data[head & (data.Length-1)];
		public T Last => data[(head+length-1) & (data.Length-1)];

		public CircularBuffer(int initialCapacity) {
			data = ArrayPool<T>.Claim(initialCapacity);
			head = 0;
			length = 0;
		}

		public CircularBuffer(T[] backingArray) {
			data = backingArray;
			head = 0;
			length = 0;
		}

		public void Clear () {
			length = 0;
			head = 0;
		}

		public void AddRange (List<T> items) {
			// TODO: Can be optimized
			for (int i = 0; i < items.Count; i++) PushEnd(items[i]);
		}

		/// <summary>Pushes a new item to the start of the buffer</summary>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public void PushStart (T item) {
			if (data == null || length >= data.Length) Grow();
			length += 1;
			head -= 1;
			this[0] = item;
		}

		/// <summary>Pushes a new item to the end of the buffer</summary>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public void PushEnd (T item) {
			if (data == null || length >= data.Length) Grow();
			length += 1;
			this[length-1] = item;
		}

		/// <summary>Pushes a new item to the start or the end of the buffer</summary>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public void Push (T item, bool toStart) {
			if (toStart) PushStart(item);
			else PushEnd(item);
		}

		/// <summary>Removes and returns the first element</summary>
		public T PopStart () {
			if (length == 0) throw new System.InvalidOperationException();
			var r = this[0];
			head++;
			length--;
			return r;
		}

		/// <summary>Removes and returns the last element</summary>
		public T PopEnd () {
			if (length == 0) throw new System.InvalidOperationException();
			var r = this[length-1];
			length--;
			return r;
		}

		/// <summary>Pops either from the start or from the end of the buffer</summary>
		public T Pop (bool fromStart) {
			if (fromStart) return PopStart();
			else return PopEnd();
		}

		/// <summary>Return either the first element or the last element</summary>
		public T GetBoundaryValue (bool start) {
			return start ? GetAbsolute(AbsoluteStartIndex) : GetAbsolute(AbsoluteEndIndex);
		}

		/// <summary>Indexes the buffer, with index 0 being the first element</summary>
		public T this[int index] {
			[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			get {
#if UNITY_EDITOR
				if ((uint)index >= length) throw new System.ArgumentOutOfRangeException();
#endif
				return data[(index+head) & (data.Length-1)];
			}
			[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
			set {
#if UNITY_EDITOR
				if ((uint)index >= length) throw new System.ArgumentOutOfRangeException();
#endif
				data[(index+head) & (data.Length-1)] = value;
			}
		}

		/// <summary>
		/// Indexes the buffer using absolute indices.
		/// When pushing to and popping from the buffer, the absolute indices do not change.
		/// So e.g. after doing PushStart(x) on an empty buffer, GetAbsolute(-1) will get the newly pushed element.
		/// </summary>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public T GetAbsolute (int index) {
#if UNITY_EDITOR
			if ((uint)(index - head) >= length) throw new System.ArgumentOutOfRangeException();
#endif
			return data[index & (data.Length-1)];
		}

		void Grow () {
			var newData = ArrayPool<T>.Claim(System.Math.Max(4, length*2));
			if (data != null) {
				var inOrderItems = data.Length - (head & (data.Length-1));
				System.Array.Copy(data, head & (data.Length-1), newData, head & (newData.Length - 1), inOrderItems);
				var wraparoundItems = length - inOrderItems;
				if (wraparoundItems > 0) System.Array.Copy(data, 0, newData, 0, wraparoundItems);
				ArrayPool<T>.Release(ref data);
			}
			data = newData;
		}

		/// <summary>
		/// Rotates the data (if necessary) so that all items are contiguous in memory.
		///
		/// TODO: Use System.Span when that is supported by all versions of Unity.
		/// </summary>
		/// <param name="data">A contiguous array of all items (may be larger than necessary).</param>
		/// <param name="startIndex">The data starts at this index in the array.</param>
		public void MakeContiguous (out T[] data, out int startIndex) {
			if (this.data == null) Grow();
			if (head < 0 || head + length >= this.data.Length) {
				var inOrderItems = this.data.Length - (head & (this.data.Length-1));
				var newData = ArrayPool<T>.Claim(this.data.Length);
				System.Array.Copy(this.data, head & (this.data.Length-1), newData, 0, inOrderItems);
				var wraparoundItems = length - inOrderItems;
				if (wraparoundItems > 0) System.Array.Copy(this.data, 0, newData, inOrderItems, wraparoundItems);
				ArrayPool<T>.Release(ref this.data);
				data = this.data = newData;
				this.head = startIndex = 0;
			} else {
				data = this.data;
				startIndex = head;
			}
		}

		/// <summary>Release the backing array of this buffer back into an array pool</summary>
		public void Pool () {
			ArrayPool<T>.Release(ref data);
			length = 0;
			head = 0;
		}
	}
}
