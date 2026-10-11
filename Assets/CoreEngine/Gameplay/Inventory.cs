// Assets/CoreEngine/Gameplay/Inventory.cs
//
// GAMEPLAY STEP 1 (task 64, GAMEPLAY_PHASE_PLAN.md picks P4/P5/P8/P12): the
// player's inventory. A plain class -- no MonoBehaviour, no world access -- so
// EditMode pins every rule without a scene (InventoryTests).
//
//   36 slots: 0-8 the hotbar (keys 1-9, wheel), 9-35 the backpack (E panel).
//   A slot is (item, count); item = the material byte it places (Items.cs),
//   0 = empty. Counts are voxels; one stack holds up to StackCap (10,000).
//   Add fills existing stacks of that item first, then the first empty slot
//   (hotbar before backpack); what does not fit is NOT added (P8: the caller
//   reports it lost).
//
// SAVED BESIDE THE WORLD'S EDITS (P12, plan guardrail 7): player.json next to
// the deltas folder, never inside D.1. ToJson/FromJson are the whole format.

using System;
using UnityEngine;

[Serializable]
public sealed class Inventory
{
    public const int HotbarSize = 9;
    public const int BackpackSize = 27;
    public const int SlotCount = HotbarSize + BackpackSize;
    public const int StackCap = 10000;
    public const int FormatVersion = 1;

    // JsonUtility: public fields only. ints, not bytes, so the file reads as numbers.
    public int version = FormatVersion;
    public int selected;
    public int[] items = new int[SlotCount];
    public int[] counts = new int[SlotCount];

    public byte ItemAt(int slot) => (byte)items[slot];
    public int CountAt(int slot) => counts[slot];
    public bool IsEmpty(int slot) => counts[slot] <= 0;

    /// The selected hotbar slot, 0-8.
    public int Selected
    {
        get => selected;
        set => selected = ((value % HotbarSize) + HotbarSize) % HotbarSize;
    }

    /// Total of `item` across every slot.
    public int Count(byte item)
    {
        int n = 0;
        for (int i = 0; i < SlotCount; i++) if (items[i] == item && counts[i] > 0) n += counts[i];
        return n;
    }

    /// Room left for `item` (existing stacks + empty slots).
    public int SpaceFor(byte item)
    {
        if (item == Items.None) return 0;
        int n = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (counts[i] <= 0) n += StackCap;
            else if (items[i] == item) n += StackCap - counts[i];
        }
        return n;
    }

    /// Adds up to `amount` of `item`; returns how many were added (the rest
    /// did not fit). Existing stacks first, then empty slots, lowest index first.
    public int Add(byte item, int amount)
    {
        if (item == Items.None || amount <= 0) return 0;
        int left = amount;
        for (int i = 0; i < SlotCount && left > 0; i++)
        {
            if (counts[i] <= 0 || items[i] != item) continue;
            int put = Math.Min(left, StackCap - counts[i]);
            counts[i] += put;
            left -= put;
        }
        for (int i = 0; i < SlotCount && left > 0; i++)
        {
            if (counts[i] > 0) continue;
            int put = Math.Min(left, StackCap);
            items[i] = item;
            counts[i] = put;
            left -= put;
        }
        return amount - left;
    }

    /// Takes `amount` from one slot, all or nothing (P7). False = too few.
    public bool TakeFromSlot(int slot, int amount)
    {
        if (amount <= 0) return true;
        if (slot < 0 || slot >= SlotCount || counts[slot] < amount) return false;
        counts[slot] -= amount;
        if (counts[slot] == 0) items[slot] = Items.None;
        return true;
    }

    /// The E panel's click-two-slots: same item merges into `b` up to the cap,
    /// anything else swaps.
    public void Swap(int a, int b)
    {
        if (a == b || a < 0 || b < 0 || a >= SlotCount || b >= SlotCount) return;
        if (counts[a] > 0 && counts[b] > 0 && items[a] == items[b])
        {
            int put = Math.Min(counts[a], StackCap - counts[b]);
            counts[b] += put;
            counts[a] -= put;
            if (counts[a] == 0) items[a] = Items.None;
            return;
        }
        (items[a], items[b]) = (items[b], items[a]);
        (counts[a], counts[b]) = (counts[b], counts[a]);
    }

    public void Clear()
    {
        Array.Clear(items, 0, SlotCount);
        Array.Clear(counts, 0, SlotCount);
        selected = 0;
    }

    /// A fresh Survival inventory: the starter kit (P10).
    public static Inventory WithStarterKit()
    {
        var inv = new Inventory();
        foreach (var (item, count) in Items.StarterKit) inv.Add(item, count);
        return inv;
    }

    public string ToJson() => JsonUtility.ToJson(this, true);

    /// Never throws. Empty or malformed -> null with `error` set; a slot with a
    /// bad item or count is emptied, counts over the cap are clamped.
    public static Inventory FromJson(string json, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "empty file"; return null; }
        Inventory inv;
        try { inv = JsonUtility.FromJson<Inventory>(json); }
        catch (Exception e) { error = "malformed JSON: " + e.Message; return null; }
        if (inv == null) { error = "malformed JSON (null result)"; return null; }

        var fixedInv = new Inventory { Selected = inv.selected };
        int bad = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            int it = inv.items != null && i < inv.items.Length ? inv.items[i] : 0;
            int c = inv.counts != null && i < inv.counts.Length ? inv.counts[i] : 0;
            if (c <= 0 && it == 0) continue;
            if (it <= 0 || it > 255 || c <= 0) { bad++; continue; }
            if (c > StackCap) { bad++; c = StackCap; }
            fixedInv.items[i] = it;
            fixedInv.counts[i] = c;
        }
        if (bad > 0) error = $"{bad} bad slot(s) fixed";
        return fixedInv;
    }
}
