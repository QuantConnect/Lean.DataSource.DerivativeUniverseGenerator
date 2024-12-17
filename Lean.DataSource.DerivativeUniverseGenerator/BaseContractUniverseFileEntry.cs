/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using QuantConnect.Data;
using QuantConnect.Data.Market;

namespace QuantConnect.DataSource.DerivativeUniverseGenerator
{
    /// <summary>
    /// Base representation of a contract universe entry
    /// </summary>
    public class BaseContractUniverseFileEntry : BaseDerivativeUniverseFileEntry
    {
        /// <summary>
        /// Contract's open interest on the processing date.
        /// </summary>
        public decimal? OpenInterest { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseContractUniverseFileEntry"/> class.
        /// </summary>
        /// <param name="symbol"></param>
        public BaseContractUniverseFileEntry(Symbol symbol)
           : base(symbol)
        {
        }

        /// <summary>
        /// Updates the contract's prices and open interest.
        /// </summary>
        public override void Update(Slice slice)
        {
            if (!Symbol.HasUnderlying)
            {
                base.Update(slice);
            }
            else
            {
                if (slice.TryGet<OpenInterest>(Symbol, out var openInterest))
                {
                    OpenInterest = openInterest.Value;
                }

                if (slice.Bars.TryGetValue(Symbol, out var tradeBar))
                {
                    Volume = tradeBar.Volume;
                }

                if (slice.QuoteBars.TryGetValue(Symbol, out var quoteBar))
                {
                    Open = quoteBar.Open;
                    High = quoteBar.High;
                    Low = quoteBar.Low;
                    Close = quoteBar.Close;
                }
            }
        }
    }
}
