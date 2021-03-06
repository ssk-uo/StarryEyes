﻿using System;
using System.Collections.Generic;
using StarryEyes.Breezy.DataModel;

namespace StarryEyes.Filters.Expressions.Operators
{
    public class FilterNegate : FilterSingleValueOperator
    {
        public override IEnumerable<FilterExpressionType> SupportedTypes
        {
            get { yield return FilterExpressionType.Boolean; }
        }

        public override Func<TwitterStatus, bool> GetBooleanValueProvider()
        {
            if (!FilterExpressionUtil.Assert(FilterExpressionType.Boolean, Value.SupportedTypes))
                throw new FilterQueryException("Negate operator must be have boolean value.", ToQuery());
            var bvp = Value.GetBooleanValueProvider();
            return _ => !bvp(_);
        }

        public override string ToQuery()
        {
            return "!" + Value.ToQuery();
        }
    }

    /// <summary>
    /// Pseudo filter.
    /// </summary>
    public class FilterBracket : FilterSingleValueOperator
    {
        private readonly FilterOperatorBase _inner;
        public FilterBracket(FilterOperatorBase inner)
        {
            this._inner = inner;
        }

        public override IEnumerable<FilterExpressionType> SupportedTypes
        {
            get
            {
                return _inner == null ? new[] { FilterExpressionType.Boolean, FilterExpressionType.Set } :
                    _inner.SupportedTypes;
            }
        }

        public override Func<TwitterStatus, bool> GetBooleanValueProvider()
        {
            return _inner == null ? Tautology : _inner.GetBooleanValueProvider();
        }

        public override Func<TwitterStatus, long> GetNumericValueProvider()
        {
            if (_inner == null)
                throw new NotSupportedException();
            return _inner.GetNumericValueProvider();
        }

        public override Func<TwitterStatus, IReadOnlyCollection<long>> GetSetValueProvider()
        {
            return _inner == null ? (_ => new List<long>()) : _inner.GetSetValueProvider();
        }

        public override Func<TwitterStatus, string> GetStringValueProvider()
        {
            if (_inner == null)
                throw new NotSupportedException();
            return _inner.GetStringValueProvider();
        }

        public override string ToQuery()
        {
            if (_inner == null)
                return "()";
            return "(" + _inner.ToQuery() + ")";
        }
    }
}
