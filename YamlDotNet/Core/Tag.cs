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

using System;

namespace YamlDotNet.Core
{
    public struct Tag : IEquatable<Tag>
    {
        public static readonly Tag NonSpecific = default;

        private readonly string? value;

        public string Value => value ?? throw new InvalidOperationException("Cannot read the Value of a non-specific tag");

        public bool IsNonSpecific => value is null;

        public Tag(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("A tag cannot be null or empty. To indicate a non-specific tag, please use Tag.NonSpecific", nameof(value));
            }

            this.value = value;
        }

        public override string ToString() => Value ?? "?";

        public bool Equals(Tag other) => Equals(value, other.value);

        public override bool Equals(object? obj)
        {
            return obj is Tag other && Equals(other);
        }

        public override int GetHashCode()
        {
            return value?.GetHashCode() ?? 0;
        }

        public static bool operator ==(Tag left, Tag right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Tag left, Tag right)
        {
            return !(left == right);
        }

        public static implicit operator Tag(string value) => new Tag(value);
    }
}