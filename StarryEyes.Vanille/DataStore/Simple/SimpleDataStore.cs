﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using StarryEyes.Vanille.Serialization;

namespace StarryEyes.Vanille.DataStore.Simple
{
    /// <summary>
    /// Simple data store implemented AbstractDataStore.
    /// </summary>
    /// <typeparam name="TKey">Key of value</typeparam>
    /// <typeparam name="TValue">actual store item</typeparam>
    public class SimpleDataStore<TKey, TValue> : DataStoreBase<TKey, TValue>
        where TKey : IComparable<TKey>
        where TValue : IBinarySerializable, new()
    {
        private object dictLock = new object();
        private SortedDictionary<TKey, TValue> dictionary;

        public SimpleDataStore(Func<TValue, TKey> keyProvider, IComparer<TKey> comparer = null)
            : base(keyProvider)
        {
            dictionary = new SortedDictionary<TKey, TValue>(comparer);
        }

        public override int Count
        {
            get { lock (dictLock) { return dictionary.Count; } }
        }

        public override void Store(TValue value)
        {
            lock (dictLock)
            {
                dictionary[GetKey(value)] = value;
            }
        }

        public override IObservable<TValue> Get(TKey key)
        {
            return Observable.Start(() =>
            {
                lock (dictLock)
                {
                    TValue value;
                    if (dictionary.TryGetValue(key, out value))
                        return Observable.Return(value);
                    else
                        return Observable.Empty<TValue>();
                }
            })
            .SelectMany(_ => _);
        }

        public override IObservable<TValue> Find(Func<TValue, bool> predicate, FindRange<TKey> range = null, int? returnLowerBound = null)
        {
            return Observable.Start(() =>
            {
                lock (dictLock)
                {
                    return dictionary.Keys
                        .CheckRange(range, _ => _)
                        .Select(_ => dictionary[_])
                        .Where(predicate)
                        .TakeIfNotNull(returnLowerBound)
                        .ToArray();
                }
            })
            .SelectMany(_ => _);
        }

        public override void Remove(TKey key)
        {
            lock (dictLock)
            {
                dictionary.Remove(key);
            }
        }
    }
}
