// <copyright file="UnaryTerm.cs" company="Basho Technologies, Inc.">
// Copyright 2011 - OJ Reeves & Jeremiah Peschka
// Copyright 2014 - Basho Technologies, Inc.
//
// This file is provided to you under the Apache License,
// Version 2.0 (the "License"); you may not use this file
// except in compliance with the License.  You may obtain
// a copy of the License at
//
//   http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
// </copyright>

namespace RiakClient.Models.Search
{
    /// <summary>
    /// Represents a Lucene "unary" search term.
    /// </summary>
    public class UnaryTerm : Term
    {
        private readonly Token value;

        /// <summary>
        /// Initializes a new instance of the <see cref="UnaryTerm"/> class.
        /// </summary>
        /// <param name="search">The fluent search to add this term to.</param>
        /// <param name="field">The field to search.</param>
        /// <param name="value">The value to search the <paramref name="field"/> for.</param>
        internal UnaryTerm(RiakFluentSearch search, string field, string value)
            : this(search, field, Token.Is(value))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UnaryTerm"/> class.
        /// </summary>
        /// <param name="search">The fluent search to add this term to.</param>
        /// <param name="field">The field to search.</param>
        /// <param name="value">The <see cref="Token"/> to search the <paramref name="field"/> for.</param>
        internal UnaryTerm(RiakFluentSearch search, string field, Token value)
            : base(search, field)
        {
            this.value = value;
        }

        /// <summary>
        /// Returns the term in a Lucene query string format.
        /// </summary>
        /// <returns>
        /// A string that represents the query term.</returns>
        public override string ToString()
        {
            return Prefix() + Field() + value + Suffix();
        }
    }
}
