//   Copyright 2011 Microsoft Corporation
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.

namespace System.Spatial
{
    using System.Runtime.Serialization;

    /// <summary>
    /// the exception thrown on an unsuccessful parsing of the serialized format.
    /// </summary>
#if !SILVERLIGHT
    [Serializable]
#endif
    public class ParseErrorException : Exception
    {
        /// <summary>
        /// Creates a ParseErrorException
        /// </summary>
        public ParseErrorException()
        {
        }

        /// <summary>
        /// Creates a ParseErrorException from a message and previous exception
        /// </summary>
        /// <param name="message">The message about the exception.</param>
        /// <param name="innerException">The exception that preceeded this one.</param>
        public ParseErrorException(string message, Exception innerException) 
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates a ParseErrorException from a message
        /// </summary>
        /// <param name="message">The message about the exception.</param>
        public ParseErrorException(string message) 
            : base(message)
        {
        }

#if !SILVERLIGHT
        /// <summary>
        /// Creates a ParseErrorException from serialized data.
        /// </summary>
        /// <param name="info">The instance that hosld the serialized object data about the exception being thrown.</param>
        /// <param name="context">The instance that contains contextual information about the source or destination.</param>
        protected ParseErrorException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {
        }
#endif
    }
}
