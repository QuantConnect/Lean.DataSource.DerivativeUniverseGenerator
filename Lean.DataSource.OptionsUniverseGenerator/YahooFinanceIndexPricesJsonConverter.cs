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

using System.Collections.Generic;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Json converter for Yahoo Finance Chart (history prices) API response
    /// </summary>
    public class YahooFinanceIndexPricesJsonConverter : JsonConverter
    {
        /// <summary>
        /// Whether this JsonConverter can read JSON.
        /// </summary>
        public override bool CanRead => true;

        /// <summary>
        /// Whether this JsonConverter can write JSON.
        /// </summary>
        public override bool CanWrite => false;

        /// <summary>
        /// Whether this JsonConverter can be used for the given type.
        /// </summary>
        /// <param name="objectType">The target conversion type</param>
        /// <returns>Whether this JsonConverter can be used for the given type</returns>
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(YahooFinanceIndexPrices);
        }

        /// <summary>
        /// Reads the JSON representation of the object.
        /// </summary>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var jObject = JObject.Load(reader);
            var jResult = jObject["chart"]["result"][0];
            var jPrices = jResult["indicators"]["quote"][0];

            if (!jPrices.HasValues)
            {
                throw new JsonReaderException("No data available for the requested symbol.");
            }

            var result = new YahooFinanceIndexPrices();
            result.Timestamps = jResult["timestamp"].ToObject<List<long>>();
            result.OpenPrices = ConvertArray<decimal>(jPrices["open"] as JArray);
            result.HighPrices = ConvertArray<decimal>(jPrices["high"] as JArray);
            result.LowPrices = ConvertArray<decimal>(jPrices["low"] as JArray);
            result.ClosePrices = ConvertArray<decimal>(jPrices["close"] as JArray);
            result.Volumes = ConvertArray<decimal>(jPrices["volume"] as JArray);

            return result;
        }

        private List<TItem> ConvertArray<TItem>(JArray jArray)
        {
            var result = new List<TItem>(jArray.Count);
            for (int i = 0; i < jArray.Count; i++)
            {
                var jToken = jArray[i];
                if (jToken.Type != JTokenType.Null)
                {
                    result.Add(jToken.ToObject<TItem>());
                }
                else if (i > 0)
                {
                    // Fill-forward missing values
                    result.Add(result[i - 1]);
                }
                else
                {
                    result.Add(default);
                }
            }

            return result;
        }

        /// <summary>
        /// Writes the JSON representation of the object.
        /// </summary>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}