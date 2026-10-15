using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game
{
    public sealed class ItemContainerSnapshot
    {
        internal ItemContainerSnapshot(int capacity, Dictionary<int, ItemStack> slots)
        {
            Capacity = capacity;
            Slots = slots;
        }

        public int Capacity { get; }
        public IReadOnlyDictionary<int, ItemStack> Slots { get; }
    }

    public sealed class ItemContainer
    {
        private Dictionary<int, ItemStack> slots;
        private ReadOnlyDictionary<int, ItemStack> readonlySlots;

        public ItemContainer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
            slots = new Dictionary<int, ItemStack>();
            readonlySlots = new ReadOnlyDictionary<int, ItemStack>(slots);
        }

        public int Capacity { get; private set; }
        public IReadOnlyDictionary<int, ItemStack> Slots => readonlySlots;

        public bool TryGet(int index, out ItemStack stack)
        {
            stack = null;
            return IsValidIndex(index) && slots.TryGetValue(index, out stack);
        }

        public int GetTotalAmount(int itemId)
        {
            if (itemId <= 0)
            {
                return 0;
            }

            return slots.Values
                .Where(stack => stack.ItemId == itemId)
                .Sum(stack => stack.Amount);
        }

        public int CalculateAcceptableAmount(int itemId, int requestedAmount, int maxStackAmount)
        {
            if (!ValidateItemInput(itemId, requestedAmount, maxStackAmount, out _))
            {
                return 0;
            }

            var remaining = requestedAmount;
            foreach (var stack in slots.OrderBy(pair => pair.Key).Select(pair => pair.Value))
            {
                if (stack.ItemId != itemId)
                {
                    continue;
                }

                remaining -= Math.Min(remaining, Math.Max(0, maxStackAmount - stack.Amount));
                if (remaining == 0)
                {
                    return requestedAmount;
                }
            }

            var emptySlots = Capacity - slots.Count;
            remaining -= Math.Min(remaining, emptySlots * maxStackAmount);
            return requestedAmount - Math.Max(0, remaining);
        }

        public bool TryAdd(int itemId, int requestedAmount, int maxStackAmount, out int acceptedAmount, out string error, ItemStack provenance=null)
        {
            acceptedAmount = 0;
            if (!ValidateItemInput(itemId, requestedAmount, maxStackAmount, out error))
            {
                return false;
            }

            var remaining = requestedAmount;
            foreach (var pair in slots.OrderBy(pair => pair.Key).ToList())
            {
                var stack = pair.Value;
                if (stack.ItemId != itemId || stack.Amount >= maxStackAmount)
                {
                    continue;
                }

                var add = Math.Min(remaining, maxStackAmount - stack.Amount);
                stack.MergeOrigin(provenance,acceptedAmount,add);
                stack.Amount += add;
                acceptedAmount += add;
                remaining -= add;
                if (remaining == 0)
                {
                    return true;
                }
            }

            for (var index = 0; index < Capacity && remaining > 0; index++)
            {
                if (slots.ContainsKey(index))
                {
                    continue;
                }

                var add = Math.Min(remaining, maxStackAmount);
                slots[index] = new ItemStack(itemId, add);
                slots[index].MergeOrigin(provenance,acceptedAmount,add);
                acceptedAmount += add;
                remaining -= add;
            }

            return acceptedAmount > 0;
        }

        public bool TryRemove(int index, int amount, out ItemStack removed, out string error)
        {
            removed = null;
            if (!IsValidIndex(index))
            {
                error = "槽位索引超出范围";
                return false;
            }

            if (amount <= 0)
            {
                error = "数量 必须为正数";
                return false;
            }

            if (!slots.TryGetValue(index, out var stack))
            {
                error = "源槽位为空";
                return false;
            }

            if (amount > stack.Amount)
            {
                error = "数量超过堆叠上限";
                return false;
            }

            removed = stack.Clone();
            removed.Amount = amount;
            removed.AcquiredAmount=stack.AcquiredSlice(0,amount);
            stack.AcquiredAmount-=removed.AcquiredAmount;
            stack.Amount -= amount;
            if (stack.Amount == 0)
            {
                slots.Remove(index);
            }

            error = null;
            return true;
        }

        public bool TryMoveWithin(int fromIndex, int toIndex, int maxStackAmount, out string error)
        {
            if (!IsValidIndex(fromIndex) || !IsValidIndex(toIndex))
            {
                error = "槽位索引超出范围";
                return false;
            }

            if (maxStackAmount <= 0)
            {
                error = "最大堆叠数量必须为正数";
                return false;
            }

            if (!slots.TryGetValue(fromIndex, out var source))
            {
                error = "源槽位为空";
                return false;
            }

            if (fromIndex == toIndex)
            {
                error = null;
                return true;
            }

            if (!slots.TryGetValue(toIndex, out var target))
            {
                slots.Remove(fromIndex);
                slots[toIndex] = source;
                error = null;
                return true;
            }

            if (source.ItemId != target.ItemId)
            {
                slots[fromIndex] = target;
                slots[toIndex] = source;
                error = null;
                return true;
            }

            var moved = Math.Min(source.Amount, maxStackAmount - target.Amount);
            if (moved <= 0)
            {
                error = "目标堆叠已满";
                return false;
            }

            var acquired=source.AcquiredSlice(0,moved);
            target.MergeOrigin(source,0,moved);
            source.AcquiredAmount-=acquired;
            target.Amount += moved;
            source.Amount -= moved;
            if (source.Amount == 0)
            {
                slots.Remove(fromIndex);
            }

            error = null;
            return true;
        }

        public bool TrySplitHalf(int fromIndex, int toIndex, out string error)
        {
            if (!IsValidIndex(fromIndex) || !IsValidIndex(toIndex))
            {
                error = "槽位索引超出范围";
                return false;
            }

            if (!slots.TryGetValue(fromIndex, out var source))
            {
                error = "源槽位为空";
                return false;
            }

            if (slots.ContainsKey(toIndex))
            {
                error = "目标槽位不为空";
                return false;
            }

            var split = source.Amount / 2;
            if (split <= 0)
            {
                error = "源数量过小";
                return false;
            }

            var acquired=source.AcquiredSlice(0,split);
            slots[toIndex] = new ItemStack(source.ItemId, split){OriginExpeditionId=source.OriginExpeditionId,AcquiredAmount=acquired};
            source.AcquiredAmount-=acquired;
            source.Amount -= split;
            error = null;
            return true;
        }

        public bool TryExpandCapacity(int newCapacity, out string error)
        {
            if (newCapacity <= Capacity)
            {
                error = "新容量必须大于当前容量";
                return false;
            }

            Capacity = newCapacity;
            error = null;
            return true;
        }

        public ItemContainerSnapshot CaptureSnapshot()
        {
            return new ItemContainerSnapshot(Capacity, CloneSlots(slots));
        }

        public void RestoreSnapshot(ItemContainerSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Capacity <= 0)
            {
                throw new ArgumentException("快照 无效", nameof(snapshot));
            }

            Capacity = snapshot.Capacity;
            slots = CloneSlots(snapshot.Slots);
            readonlySlots = new ReadOnlyDictionary<int, ItemStack>(slots);
        }

        public static bool TryTransfer(
            ItemContainer source,
            int sourceIndex,
            ItemContainer target,
            int targetIndex,
            Func<int, int> maxStackResolver,
            out string error)
        {
            if (source == null || target == null)
            {
                error = "容器为空";
                return false;
            }

            if (ReferenceEquals(source, target))
            {
                if (!source.TryGet(sourceIndex, out var sameSource))
                {
                    error = "源槽位为空";
                    return false;
                }

                return source.TryMoveWithin(sourceIndex, targetIndex, ResolveMaxStack(maxStackResolver, sameSource.ItemId), out error);
            }

            var sourceSnapshot = source.CaptureSnapshot();
            var targetSnapshot = target.CaptureSnapshot();
            var success = false;
            try
            {
                if (!source.IsValidIndex(sourceIndex) || !target.IsValidIndex(targetIndex))
                {
                    error = "槽位索引超出范围";
                    return false;
                }

                if (!source.slots.TryGetValue(sourceIndex, out var sourceStack))
                {
                    error = "源槽位为空";
                    return false;
                }

                var sourceMax = ResolveMaxStack(maxStackResolver, sourceStack.ItemId);
                if (sourceMax <= 0)
                {
                    error = "最大堆叠解析器失败";
                    return false;
                }

                if (!target.slots.TryGetValue(targetIndex, out var targetStack))
                {
                    source.slots.Remove(sourceIndex);
                    target.slots[targetIndex] = sourceStack;
                    success = true;
                    error = null;
                    return true;
                }

                var targetMax = ResolveMaxStack(maxStackResolver, targetStack.ItemId);
                if (targetMax <= 0)
                {
                    error = "最大堆叠解析器失败";
                    return false;
                }

                if (sourceStack.ItemId != targetStack.ItemId)
                {
                    source.slots[sourceIndex] = targetStack;
                    target.slots[targetIndex] = sourceStack;
                    success = true;
                    error = null;
                    return true;
                }

                var moved = Math.Min(sourceStack.Amount, sourceMax - targetStack.Amount);
                if (moved <= 0)
                {
                    error = "目标堆叠已满";
                    return false;
                }

                var acquired=sourceStack.AcquiredSlice(0,moved);
                targetStack.MergeOrigin(sourceStack,0,moved);
                sourceStack.AcquiredAmount-=acquired;
                targetStack.Amount += moved;
                sourceStack.Amount -= moved;
                if (sourceStack.Amount == 0)
                {
                    source.slots.Remove(sourceIndex);
                }

                success = true;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                if (!success)
                {
                    source.RestoreSnapshot(sourceSnapshot);
                    target.RestoreSnapshot(targetSnapshot);
                }
            }
        }

        private bool IsValidIndex(int index)
        {
            return index >= 0 && index < Capacity;
        }

        private static int ResolveMaxStack(Func<int, int> resolver, int itemId)
        {
            return resolver == null ? 0 : resolver(itemId);
        }

        private static bool ValidateItemInput(int itemId, int amount, int maxStackAmount, out string error)
        {
            if (itemId <= 0)
            {
                error = "ItemId 必须为正数";
                return false;
            }

            if (amount <= 0)
            {
                error = "数量 必须为正数";
                return false;
            }

            if (maxStackAmount <= 0)
            {
                error = "最大堆叠数量必须为正数";
                return false;
            }

            error = null;
            return true;
        }

        private static Dictionary<int, ItemStack> CloneSlots(IReadOnlyDictionary<int, ItemStack> source)
        {
            return source.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        }
    }
}
