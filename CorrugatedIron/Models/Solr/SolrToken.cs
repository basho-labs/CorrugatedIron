// Copyright (c) 2010 - OJ Reeves & Jeremiah Peschka
// 
// This file is provided to you under the Apache License,
// Version 2.0 (the "License"); you may not use this file
// except in compliance with the License.  You may obtain
// a copy of the License at
// 
//    http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.

using System;
using System.Text;
using System.Text.RegularExpressions;

namespace CorrugatedIron.Models.Solr
{
    public class SolrToken : ISolrQueryPart
    {
        public string Field { get; set; }
        public string Term { get; set; }
        public int? Proximity { get; set; } 
        public decimal? Boost { get; set; }
        public bool Required { get; set; }
        public bool Prohibit { get; set; }
        
        public SolrToken ()
        {
            Required = false;
            Prohibit = false;
        }
        
        public override string ToString() {
            return ToSolrQueryString();
        }

        public string ToSolrQueryString()
        {
            var sb = new StringBuilder();
            
            if (!String.IsNullOrEmpty(Field)) {
                sb.Append(Field);
                sb.Append(":");
            }
            
            if (Required) {
                sb.Append("+");
            }
            
            if (Prohibit) {
                sb.Append("-");
            }
            
            sb.Append(EscapeTerm(Term));
            
            if (Proximity.HasValue) {
                sb.Append(String.Format("~{0}", Proximity));
            }
            
            if (Boost.HasValue) {
                sb.Append(String.Format("^{0}", Boost));
            }
            
            return sb.ToString();
        }
        
        protected string EscapeTerm(string term) {
            // + - && || ! ( ) { } [ ] ^ " ~ * ? : \
            string pattern = @"[\+\-!\(\)\{\}\[\]^\""~\*\?\:\\]{1}";
            
            string replacement = @"\$&";
            
            var regex = new Regex(pattern);
            var result = regex.Replace(Term, replacement);
            
            // If we have a phrase, then we want to put double quotes around the Term
            if (Term.IndexOf(" ") > -1) {
                result = String.Format("\"{0}\"", result);
            }
            
            return result;
        }
    }
}

