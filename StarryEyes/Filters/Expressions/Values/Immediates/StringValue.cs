﻿using System;
using System.Collections.Generic;
using StarryEyes.Breezy.DataModel;

namespace StarryEyes.Filters.Expressions.Values.Immediates
{
    public class StringValue : ValueBase
    {
        private readonly string _value;

        public StringValue(string value)
        {
            this._value = value;
        }

        public override IEnumerable<FilterExpressionType> SupportedTypes
        {
            get
            {
                yield return FilterExpressionType.String;
            }
        }

        public override Func<TwitterStatus, string> GetStringValueProvider()
        {
            return _ => _value;
        }

        public override string ToQuery()
        {
            return "\"" + _value + "\"";
        }
    }
}
