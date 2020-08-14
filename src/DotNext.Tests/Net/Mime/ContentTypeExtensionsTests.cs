﻿using System.Net.Mime;
using System.Text;
using Xunit;

namespace DotNext.Net.Mime
{
    public sealed class ContentTypeExtensionsTests : Test
    {
        [Fact]
        public static void ParseVariousEncodings()
        {
            var encoding = new ContentType("plain/text; charset=utf-32").GetEncoding();
            Equal(Encoding.UTF32.WebName, encoding.WebName);
            encoding = new ContentType("plain/text; charset=utf-7").GetEncoding();
            Equal(Encoding.UTF7.WebName, encoding.WebName);
        }
    }
}
