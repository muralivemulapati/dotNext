﻿using System.Collections.Generic;
using Xunit;

namespace DotNext.Reflection
{
    public sealed class CollectionTypeTests : Test
    {
        [Fact]
        public static void GetItemTypeTest()
        {
            Equal(typeof(long), typeof(long[]).GetItemType());
            Equal(typeof(bool), typeof(IList<bool>).GetItemType());
        }
    }
}
