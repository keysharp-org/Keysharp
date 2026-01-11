namespace Keysharp.Core.Common.ObjectBase
{
	internal class OwnPropsIteratorData : BaseIteratorData<OwnPropsIterator>
	{
	}

	internal class OwnPropsIterator : KeysharpEnumerator, IEnumerator<(object, object)>
	{
		private readonly Dictionary<object, object> map;
		private readonly KeysharpObject obj;
		private IEnumerator<KeyValuePair<object, object>> iter;

		public (object, object) Current
		{
			get
			{
				var kv = iter.Current;

				if (GetVal)
				{
					if (kv.Value is OwnPropsDesc op)
					{
						if (op.Value != null)
							return (kv.Key, op.Value);
						else if (op.Get != null && op.Get is FuncObj fo)
							return (kv.Key, fo.Call(obj));
						else if (op.Call != null)
							return (kv.Key, op.Call);
					}

					if (kv.Value is MethodPropertyHolder mph)
						return (kv.Key, mph.CallFunc(obj, null));
					else if (kv.Value is FuncObj fo)//ParamLength was verified when this was created in OwnProps().
						return (kv.Key, fo.Call(obj));
					else
						return (kv.Key, kv.Value);
				}

				return (kv.Key, null);
			}
		}

		object IEnumerator.Current => Current;
		internal new int Count => GetVal ? 2 : 1;
		private bool _getVal = false;
		internal bool GetVal
		{
			get => _getVal;

			set
			{
				if (_getVal != value)
				{
					_getVal = value;
					var p = value ? Script.TheScript.OwnPropsIteratorData.p2 : Script.TheScript.OwnPropsIteratorData.p1;
					fo = (FuncObj)p.Clone();
					fo.Inst = this;
					CallFunc = fo;
				}
			}
		}

		internal OwnPropsIterator(KeysharpObject o, Dictionary<object, object> m, bool gv)
			: base(null, gv ? 2 : 1)
		{
			obj = o;
			map = m;
			_getVal = !gv; //Force the next line to actually change internal state
			GetVal = gv;
			iter = map.GetEnumerator();
		}

		public override object Call([ByRef] object obj0)
		{
			if (MoveNext())
			{
				GetVal = false;
				Script.SetPropertyValue(obj0, "__Value", Current.Item1);
				return true;
			}

			return false;
		}

		public override object Call([ByRef] object obj0, [ByRef] object obj1 = null)
		{
			GetVal = obj1 != null;

			if (MoveNext())
			{
				if (GetVal)
				{
					Script.SetPropertyValue(obj0, "__Value", Current.Item1);
					Script.SetPropertyValue(obj1, "__Value", Current.Item2);
				} 
				else
					Script.SetPropertyValue(obj0, "__Value", Current.Item1);
				return true;
			}

			return false;
		}

        public void Dispose() => Reset();

		public bool MoveNext() => iter.MoveNext();

		public void Reset() => iter = map.GetEnumerator();
	}

	public class OwnPropsDesc
	{
		public Any Parent { get; private set; }
		public object Value;
		public object Get;
		public object Set;
		public object Call;

		internal OwnPropsMapType Type
		{
			get
			{
				var desc = OwnPropsMapType.None;
				if (Value != null) desc |= OwnPropsMapType.Value;
				if (Get != null) desc |= OwnPropsMapType.Get;
				if (Set != null) desc |= OwnPropsMapType.Set;
				if (Call != null) desc |= OwnPropsMapType.Call;
				return desc;
			}
		}

		public OwnPropsDesc()
		{
			Parent = null;
		}

		public OwnPropsDesc(Any kso, object set_Value = null, object set_Get = null, object set_Set = null, object set_Call = null)
		{
			Parent = kso;
			Value = set_Value;
			Get = set_Get;
			Set = set_Set;
			Call = set_Call;
		}


		public OwnPropsDesc(Any kso, Map map)
		{
			Parent = kso;
			Merge(map);
		}

		public bool IsEmpty
		{
			get => Value == null && Get == null && Set == null && Call == null;
		}

		internal void Merge(Dictionary<string, OwnPropsDesc> map)
		{
			foreach ((var key, var desc) in map)
			{
				switch (key.ToUpper())
				{
					case "VALUE":
						Value = desc.Value;
						Get = null;
						Set = null;
						Call = null;
						break;

					case "GET":
						Get = desc.Get;
						Value = null;
						break;

					case "SET":
						Set = desc.Set;
						Value = null;
						break;

					case "CALL":
						Call = desc.Call;
						Value = null;
						break;
				}
			}
		}

		internal void MergeOwnPropsValues(Dictionary<string, OwnPropsDesc> map)
		{
			foreach ((var name, var desc) in map)
			{
				switch (name.ToUpper())
				{
					case "VALUE":
						Value = desc.Get ?? desc.Value;
						Get = null;
						Set = null;
						Call = null;
						break;

					case "GET":
						Get = desc.Get ?? desc.Value;
						Value = null;
						break;

					case "SET":
						Set = desc.Get ?? desc.Value;
						Value = null;
						break;

					case "CALL":
						Call = desc.Get ?? desc.Value;
						Value = null;
						break;
				}
			}
		}

		internal void Merge(OwnPropsDesc opd)
		{
			if (opd.Value != null)
				Value = opd.Value;

			if (opd.Get != null)
				Get = opd.Get;

			if (opd.Set != null)
				Set = opd.Set;

			if (opd.Call != null)
				Call = opd.Call;
		}

		public void Merge(Map map)
		{
			foreach ((var key, var value) in map.map)
			{
				switch (key.ToString().ToUpper())
				{
					case "VALUE":
						Value = value;
						Get = null;
						Set = null;
						Call = null;
						break;

					case "GET":
						Get = value;
						Value = null;
						break;

					case "SET":
						Set = value;
						Value = null;
						break;

					case "CALL":
						Call = value;
						Value = null;
						break;
				}
			}
		}

		public KeysharpObject GetDesc()
		{
			var map = new KeysharpObject();
			map.EnsureOwnProps();

			if (Value != null)
				map.DefinePropInternal("value", new OwnPropsDesc(map, Value));

			if (Get != null)
				map.DefinePropInternal("get", new OwnPropsDesc(map, Get));

			if (Set != null)
				map.DefinePropInternal("set", new OwnPropsDesc(map, Set));

			if (Call != null)
				map.DefinePropInternal("call", new OwnPropsDesc(map, Call));

			return map;
		}
	}
}