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

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Options Universe generator utils
    /// </summary>
    public static class OptionsUniverseGeneratorUtils
    {
        /// <summary>
        /// Returns the mirror option symbol for the provided option symbol.
        /// </summary>
        public static Symbol GetMirrorOptionSymbol(Symbol symbol)
        {
            // This should not be called for non-option symbols, but let's be friendly/safe
            if (!symbol.SecurityType.IsOption())
            {
                return symbol;
            }

            return Symbol.CreateOption(symbol.Underlying,
                symbol.ID.Symbol,
                symbol.ID.Market,
                symbol.ID.OptionStyle,
                symbol.ID.OptionRight == OptionRight.Call ? OptionRight.Put : OptionRight.Call,
                symbol.ID.StrikePrice,
                symbol.ID.Date);
        }
    }
}