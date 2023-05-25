namespace Pathfinding.Util {
	using Unity.Mathematics;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;

	/// <summary>
	/// A tiny slab allocator.
	/// Allocates spans of type T in power-of-two sized blocks.
	///
	/// Note: This allocator has no support for merging adjacent freed blocks.
	/// Therefore it is best suited for similarly sized allocations which are relatively small.
	///
	/// Can be used in burst jobs.
	/// </summary>
	public struct SlabAllocator<T> where T : unmanaged {
		public const int MaxAllocationSizeLog2 = 10;
		const uint UsedBit = 1u << 31;
		const uint AllocatedBit = 1u << 30;
		const uint LengthMask = AllocatedBit - 1;

		[NativeDisableUnsafePtrRestriction]
		unsafe AllocatorData* data;

		struct AllocatorData {
			public UnsafeList<byte> mem;
			public unsafe fixed int freeHeads[MaxAllocationSizeLog2+1];
		}

		struct Header {
			public uint length;
		}

		struct NextBlock {
			public int next;
		}

		public bool IsCreated {
			get {
				unsafe {
					return data != null;
				}
			}
		}

		public int ByteSize {
			get {
				unsafe {
					return data->mem.Length;
				}
			}
		}

		public SlabAllocator(int initialCapacityBytes, AllocatorManager.AllocatorHandle allocator) {
			unsafe {
				data = AllocatorManager.Allocate<AllocatorData>(allocator);
				data->mem = new UnsafeList<byte>(initialCapacityBytes, allocator);
				Clear();
			}
		}

		/// <summary>
		/// Frees all existing allocations.
		/// Does not free the underlaying unmanaged memory. Use <see cref="Dispose"/> for that.
		/// </summary>
		public void Clear () {
			CheckDisposed();
			unsafe {
				data->mem.Clear();
				for (int i = 0; i < MaxAllocationSizeLog2 + 1; i++) {
					data->freeHeads[i] = -1;
				}
			}
		}


		/// <summary>
		/// Get the span representing the given allocation.
		/// The returned array does not need to be disposed.
		/// It is only valid until the next call to <see cref="Allocate"/>, <see cref="Free"/> or <see cref="Dispose"/>.
		/// </summary>
		public NativeArray<T> GetSpan (int allocatedIndex) {
			CheckDisposed();
			unsafe {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				if (allocatedIndex < sizeof(Header) || allocatedIndex >= data->mem.Length) throw new System.IndexOutOfRangeException();
#endif
				var ptr = data->mem.Ptr + allocatedIndex;
				var header = (Header*)(ptr - sizeof(Header));
				var length = header->length & LengthMask;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				if (length < 0 || length >= 1 << MaxAllocationSizeLog2) throw new System.Exception("Invalid index");
				if ((header->length & AllocatedBit) == 0) throw new System.Exception("Trying to get a span for an unallocated index");
#endif
				var res = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(ptr, (int)length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref res, AtomicSafetyHandle.GetTempUnsafePtrSliceHandle());
#endif
				return res;
			}
		}

		internal static int ElementsToSizeIndex (int nElements) {
			if (nElements <= 0) throw new System.Exception("SlabAllocator cannot allocate less than 1 element");
			var tmp = nElements;
			int log2 = 0;
			while (tmp > 1) {
				tmp >>= 1;
				log2++;
			}
			// If nElements was not a power of two, then we need to round up to the next power of two.
			if (nElements > (1 << log2)) log2++;
			if (log2 > MaxAllocationSizeLog2) throw new System.Exception("SlabAllocator cannot allocate more than 2^MaxAllocationSizeLog2 elements");
			return log2;
		}

		/// <summary>
		/// Allocates an array big enough to fit the given values and copies them to the new allocation.
		/// Returns: An ID for the new allocation.
		/// </summary>
		public int Allocate (NativeArray<T> values, int start, int length) {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			if (length < 0 || start < 0 || start + length > values.Length) throw new System.IndexOutOfRangeException();
#endif
			var index = Allocate(length);
			var targetSpan = GetSpan(index);
			values.CopyTo(targetSpan);
			return index;
		}

		/// <summary>
		/// Allocates an array big enough to fit the given values and copies them to the new allocation.
		/// Returns: An ID for the new allocation.
		/// </summary>
		public int Allocate (NativeList<T> values) {
			var index = Allocate(values.Length);
			values.AsArray().CopyTo(GetSpan(index));
			return index;
		}

		/// <summary>
		/// Allocates an array of type T with length nElements.
		/// Must later be freed using <see cref="Free"/> (or <see cref="Dispose)"/>.
		///
		/// Returns: An ID for the new allocation.
		/// </summary>
		public int Allocate (int nElements) {
			CheckDisposed();
			var sizeIndex = ElementsToSizeIndex(nElements);
			unsafe {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				if (sizeIndex < 0 || sizeIndex > MaxAllocationSizeLog2) throw new System.Exception("Invalid size index " + sizeIndex);
#endif
				int head = data->freeHeads[sizeIndex];
				if (head != -1) {
					var ptr = data->mem.Ptr;
					data->freeHeads[sizeIndex] = ((NextBlock*)(ptr + head))->next;
					*(Header*)(ptr + head - sizeof(Header)) = new Header { length = (uint)nElements | UsedBit | AllocatedBit };
					return head;
				}

				int headerStart = data->mem.Length;
				int requiredSize = headerStart + sizeof(Header) + (1 << sizeIndex)*sizeof(T);
				if (Unity.Burst.CompilerServices.Hint.Unlikely(requiredSize > data->mem.Capacity)) {
					data->mem.SetCapacity(math.max(data->mem.Capacity*2, requiredSize));
				}

				// Set the length field directly because we know we don't have to resize the list,
				// and we do not care about zeroing the memory.
				data->mem.m_length = requiredSize;
				*(Header*)(data->mem.Ptr + headerStart) = new Header { length = (uint)nElements | UsedBit | AllocatedBit };
				return headerStart + sizeof(Header);
			}
		}

		/// <summary>Frees a single allocation</summary>
		public void Free (int allocatedIndex) {
			CheckDisposed();
			unsafe {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				if (allocatedIndex < sizeof(Header) || allocatedIndex >= data->mem.Length) throw new System.IndexOutOfRangeException();
#endif
				var ptr = data->mem.Ptr;
				var header = (Header*)(ptr + allocatedIndex - sizeof(Header));
				var length = (int)(header->length & LengthMask);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				if (length < 0 || length >= 1 << MaxAllocationSizeLog2) throw new System.Exception("Invalid index");
				if ((header->length & AllocatedBit) == 0) throw new System.Exception("Trying to free an already freed index");
#endif

				var sizeIndex = ElementsToSizeIndex(length);

				*(NextBlock*)(ptr + allocatedIndex) = new NextBlock {
					next = data->freeHeads[sizeIndex]
				};
				data->freeHeads[sizeIndex] = allocatedIndex;
				// Mark as not allocated
				header->length &= ~(AllocatedBit | UsedBit);
			}
		}

		public void CopyTo (SlabAllocator<T> other) {
			CheckDisposed();
			other.CheckDisposed();
			unsafe {
				other.data->mem.CopyFrom(data->mem);
				for (int i = 0; i < MaxAllocationSizeLog2 + 1; i++) {
					other.data->freeHeads[i] = data->freeHeads[i];
				}
			}
		}

		void CheckDisposed () {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			unsafe {
				if (data == null) throw new System.InvalidOperationException("SlabAllocator is already disposed or not initialized");
			}
#endif
		}

		/// <summary>Frees all unmanaged memory associated with this container</summary>
		public void Dispose () {
			CheckDisposed();
			unsafe {
				var allocator = data->mem.Allocator;
				data->mem.Dispose();
				AllocatorManager.Free(allocator, data);
				data = null;
			}
		}
	}
}
