using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Portable.Xaml.Helpers
{
    class ThreadSafeDictionary<TKey, TValue>
    {
		private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
	    private readonly Dictionary<TKey, TValue> _dic = new Dictionary<TKey, TValue>();
	    private static readonly bool _isNullReferenceExceptionPossible = !typeof(TKey).GetTypeInfo().IsValueType;
	    public TValue this[TKey key]
	    {
		    get
		    {
			    if (_isNullReferenceExceptionPossible && key.Equals(default(TKey)))
				    throw new ArgumentNullException();
			    TValue value;
			    _lock.EnterReadLock();
			    var result = _dic.TryGetValue(key, out value);
				_lock.ExitReadLock();
			    if (!result)
				    throw new KeyNotFoundException();
			    return value;
		    }
		    set
		    {
				if (_isNullReferenceExceptionPossible && key.Equals(default(TKey)))
					throw new ArgumentNullException();
				_lock.EnterWriteLock();
			    _dic[key] = value;
				_lock.ExitWriteLock();
		    }
	    }

	    public bool ContainsKey(TKey key)
	    {
			if (_isNullReferenceExceptionPossible && key.Equals(default(TKey)))
				throw new ArgumentNullException();
			_lock.EnterReadLock();
		    var result = _dic.ContainsKey(key);
			_lock.ExitReadLock();
		    return result;
	    }

	    public bool TryGetValue(TKey key, out TValue value)
	    {
			if (_isNullReferenceExceptionPossible && key.Equals(default(TKey)))
				throw new ArgumentNullException();
		    _lock.EnterReadLock();
		    var result = _dic.TryGetValue(key, out value);
		    _lock.ExitWriteLock();
		    return result;
		}

    }
}
