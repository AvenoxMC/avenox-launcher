using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EaglerLauncher
{
	// Minimal uncompressed big-endian NBT codec, enough to edit servers.dat while
	// keeping every field the game wrote (icons, flags...) untouched.
	class NbtTag
	{
		public byte Type;
		public object Value;  // primitives, byte[] raw arrays, string, List<NbtTag> (list), NbtCompound
		public byte ListType;

		public NbtTag(byte type, object value) { Type = type; Value = value; }
	}

	class NbtCompound
	{
		public readonly List<KeyValuePair<string, NbtTag>> Entries = new List<KeyValuePair<string, NbtTag>>();

		public NbtTag Get(string name)
		{
			foreach (var e in Entries) if (e.Key == name) return e.Value;
			return null;
		}

		public void Set(string name, NbtTag tag)
		{
			for (int i = 0; i < Entries.Count; i++)
			{
				if (Entries[i].Key == name) { Entries[i] = new KeyValuePair<string, NbtTag>(name, tag); return; }
			}
			Entries.Add(new KeyValuePair<string, NbtTag>(name, tag));
		}

		public void Remove(string name) { Entries.RemoveAll(e => e.Key == name); }

		public string GetString(string name)
		{
			var t = Get(name);
			return t != null && t.Type == 8 ? (string)t.Value : null;
		}
	}

	static class Nbt
	{
		public static NbtCompound Read(byte[] data)
		{
			using (var r = new BinaryReader(new MemoryStream(data)))
			{
				if (r.ReadByte() != 10) throw new InvalidDataException("servers.dat invalide");
				ReadString(r);
				return (NbtCompound)ReadPayload(r, 10).Value;
			}
		}

		public static byte[] Write(NbtCompound root)
		{
			var ms = new MemoryStream();
			using (var w = new BinaryWriter(ms))
			{
				w.Write((byte)10);
				WriteString(w, "");
				WritePayload(w, new NbtTag(10, root));
			}
			return ms.ToArray();
		}

		static int BE32(BinaryReader r) { var b = r.ReadBytes(4); return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]; }
		static short BE16(BinaryReader r) { var b = r.ReadBytes(2); return (short)((b[0] << 8) | b[1]); }
		static void BE32(BinaryWriter w, int v) { w.Write(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }); }
		static void BE16(BinaryWriter w, int v) { w.Write(new[] { (byte)(v >> 8), (byte)v }); }

		static string ReadString(BinaryReader r)
		{
			int n = (ushort)BE16(r);
			return Encoding.UTF8.GetString(r.ReadBytes(n));
		}

		static void WriteString(BinaryWriter w, string s)
		{
			var b = Encoding.UTF8.GetBytes(s);
			BE16(w, b.Length);
			w.Write(b);
		}

		static NbtTag ReadPayload(BinaryReader r, byte type)
		{
			switch (type)
			{
				case 1: return new NbtTag(1, r.ReadSByte());
				case 2: return new NbtTag(2, BE16(r));
				case 3: return new NbtTag(3, BE32(r));
				case 4: return new NbtTag(4, r.ReadBytes(8));
				case 5: return new NbtTag(5, r.ReadBytes(4));
				case 6: return new NbtTag(6, r.ReadBytes(8));
				case 7: return new NbtTag(7, r.ReadBytes(BE32(r)));
				case 8: return new NbtTag(8, ReadString(r));
				case 9:
					{
						byte et = r.ReadByte();
						int n = BE32(r);
						var items = new List<NbtTag>();
						for (int i = 0; i < n; i++) items.Add(ReadPayload(r, et));
						var t = new NbtTag(9, items);
						t.ListType = et;
						return t;
					}
				case 10:
					{
						var c = new NbtCompound();
						for (;;)
						{
							byte ct = r.ReadByte();
							if (ct == 0) return new NbtTag(10, c);
							string name = ReadString(r);
							c.Entries.Add(new KeyValuePair<string, NbtTag>(name, ReadPayload(r, ct)));
						}
					}
				case 11: return new NbtTag(11, r.ReadBytes(BE32(r) * 4));
				case 12: return new NbtTag(12, r.ReadBytes(BE32(r) * 8));
			}
			throw new InvalidDataException("NBT invalide (type " + type + ")");
		}

		static void WritePayload(BinaryWriter w, NbtTag t)
		{
			switch (t.Type)
			{
				case 1: w.Write((sbyte)t.Value); break;
				case 2: BE16(w, (short)t.Value); break;
				case 3: BE32(w, (int)t.Value); break;
				case 4: case 5: case 6: w.Write((byte[])t.Value); break;
				case 7: BE32(w, ((byte[])t.Value).Length); w.Write((byte[])t.Value); break;
				case 8: WriteString(w, (string)t.Value); break;
				case 9:
					{
						var items = (List<NbtTag>)t.Value;
						w.Write(items.Count == 0 ? (byte)0 : t.ListType);
						BE32(w, items.Count);
						foreach (var i in items) WritePayload(w, i);
						break;
					}
				case 10:
					foreach (var e in ((NbtCompound)t.Value).Entries)
					{
						w.Write(e.Value.Type);
						WriteString(w, e.Key);
						WritePayload(w, e.Value);
					}
					w.Write((byte)0);
					break;
				case 11: BE32(w, ((byte[])t.Value).Length / 4); w.Write((byte[])t.Value); break;
				case 12: BE32(w, ((byte[])t.Value).Length / 8); w.Write((byte[])t.Value); break;
			}
		}
	}
}
