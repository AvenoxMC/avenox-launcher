using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace EaglerLauncher
{
	// Thin helpers over JavaScriptSerializer: objects are Dictionary<string, object>,
	// arrays are object[] and numbers are int/long/decimal.
	static class Json
	{
		static JavaScriptSerializer Serializer()
		{
			var s = new JavaScriptSerializer();
			s.MaxJsonLength = int.MaxValue;
			s.RecursionLimit = 512;
			return s;
		}

		public static object Parse(string text) { return Serializer().DeserializeObject(text); }
		public static T Parse<T>(string text) { return Serializer().Deserialize<T>(text); }
		public static string Write(object value) { return Serializer().Serialize(value); }

		public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }

		public static object[] Arr(object o)
		{
			var a = o as object[];
			if (a != null) return a;
			var l = o as System.Collections.ArrayList;
			return l != null ? l.ToArray() : new object[0];
		}

		public static object Get(object o, params string[] path)
		{
			foreach (var key in path)
			{
				var d = Obj(o);
				if (d == null || !d.TryGetValue(key, out o)) return null;
			}
			return o;
		}

		public static string Str(object o, params string[] path)
		{
			var v = Get(o, path);
			return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
		}

		public static long Long(object o, params string[] path)
		{
			var v = Get(o, path);
			return v == null ? -1 : Convert.ToInt64(v, CultureInfo.InvariantCulture);
		}
	}
}
