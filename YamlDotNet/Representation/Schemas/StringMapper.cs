﻿//  This file is part of YamlDotNet - A .NET library for YAML.
//  Copyright (c) Antoine Aubry and contributors

//  Permission is hereby granted, free of charge, to any person obtaining a copy of
//  this software and associated documentation files (the "Software"), to deal in
//  the Software without restriction, including without limitation the rights to
//  use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
//  of the Software, and to permit persons to whom the Software is furnished to do
//  so, subject to the following conditions:

//  The above copyright notice and this permission notice shall be included in all
//  copies or substantial portions of the Software.

//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//  SOFTWARE.

using YamlDotNet.Core;

namespace YamlDotNet.Representation.Schemas
{
    /// <summary>
    /// The tag:yaml.org,2002:str tag, as specified in the Failsafe schema.
    /// </summary>
    /// <remarks>
    /// Use <see cref="System.String" /> as native representation.
    /// </remarks>
    public sealed class StringMapper : NodeMapper<string>
    {
        public static readonly StringMapper Default = new StringMapper(YamlTagRepository.String);

        public StringMapper(TagName tag) : base(tag) { }

        public override string Construct(Node node)
        {
            return node.Expect<Scalar>().Value;
        }

        public override Node Represent(string native, ISchemaIterator iterator)
        {
            return new Scalar(this, native);
        }
    }
}
