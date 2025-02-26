//-----------------------------------------------------------------------------
// <copyright file="ODataResourceValueSerializer.cs" company=".NET Foundation">
//      Copyright (c) .NET Foundation and Contributors. All rights reserved. 
//      See License.txt in the project root for license information.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNet.OData.Common;
using Microsoft.OData;
using Microsoft.OData.Edm;

namespace Microsoft.AspNet.OData.Formatter.Serialization
{
    /// <summary>
    /// Represents an <see cref="ODataSerializer"/> for serializing <see cref="IEdmComplexType" />'s.
    /// </summary>
    public class ODataResourceValueSerializer : ODataEdmTypeSerializer
    {
        /// <summary>
        /// Initializes a new instance of <see cref="ODataResourceValueSerializer"/>.
        /// </summary>
        public ODataResourceValueSerializer(ODataSerializerProvider serializerProvider)
            : base(ODataPayloadKind.Resource, serializerProvider)
        {
            if (serializerProvider == null)
            {
                throw Error.ArgumentNull("serializerProvider");
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ODataEdmTypeSerializer"/> class.
        /// </summary>
        /// <param name="payloadKind">The kind of OData payload that this serializer generates.</param>
        /// <param name="serializerProvider">The <see cref="ODataSerializerProvider"/> to use to write inner objects.</param>
        protected ODataResourceValueSerializer(ODataPayloadKind payloadKind, ODataSerializerProvider serializerProvider)
            : base(payloadKind, serializerProvider)
        {
            if (serializerProvider == null)
            {
                throw Error.ArgumentNull("serializerProvider");
            }
          
        }

        /// <inheritdoc/>
        public override void WriteObject(object graph, Type type, ODataMessageWriter messageWriter, ODataSerializerContext writeContext)
        {
            if (messageWriter == null)
            {
                throw Error.ArgumentNull("messageWriter");
            }
            if (writeContext == null)
            {
                throw Error.ArgumentNull("writeContext");
            }
            if (writeContext.RootElementName == null)
            {
                throw Error.Argument("writeContext", SRResources.RootElementNameMissing, typeof(ODataSerializerContext).Name);
            }

            IEdmTypeReference edmType = writeContext.GetEdmType(graph, type);
            Contract.Assert(edmType != null);

            messageWriter.WriteProperty(CreateProperty(graph, edmType, writeContext.RootElementName, writeContext));
        }

        /// <inheritdoc/>
        public override Task WriteObjectAsync(object graph, Type type, ODataMessageWriter messageWriter, ODataSerializerContext writeContext)
        {
            if (messageWriter == null)
            {
                throw Error.ArgumentNull("messageWriter");
            }
            if (writeContext == null)
            {
                throw Error.ArgumentNull("writeContext");
            }
            if (writeContext.RootElementName == null)
            {
                throw Error.Argument("writeContext", SRResources.RootElementNameMissing, typeof(ODataSerializerContext).Name);
            }

            IEdmTypeReference edmType = writeContext.GetEdmType(graph, type);
            Contract.Assert(edmType != null);

            return messageWriter.WritePropertyAsync(CreateProperty(graph, edmType, writeContext.RootElementName, writeContext));
        }

        /// <inheritdoc/>
        public sealed override ODataValue CreateODataValue(object graph, IEdmTypeReference expectedType, ODataSerializerContext writeContext)
        {
            if (!expectedType.IsStructured())
            {
                throw Error.InvalidOperation(SRResources.CannotWriteType, typeof(ODataResourceValueSerializer), expectedType.FullName());
            }

            if (graph == null)
            {
                return new ODataNullValue();
            }

            ODataResourceValue value = CreateODataResourceValue(graph, expectedType as IEdmStructuredTypeReference, writeContext);
            if (value == null)
            {
                return new ODataNullValue();
            }

            return value;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "edmTypeSerializer")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        private ODataResourceValue CreateODataResourceValue(object graph, IEdmStructuredTypeReference expectedType, ODataSerializerContext writeContext)
        {
            List<ODataProperty> properties = new List<ODataProperty>();
            ODataResourceValue resourceValue = new ODataResourceValue { TypeName = writeContext.GetEdmType(graph, graph.GetType()).FullName() };
  
            IDelta delta = graph as IDelta;
            if (delta != null)
            {
                foreach (string propertyName in delta.GetChangedPropertyNames())
                {
                    SetDeltaPropertyValue(writeContext, properties, delta, propertyName);
                }

                foreach (string propertyName in delta.GetUnchangedPropertyNames())
                {
                    SetDeltaPropertyValue(writeContext, properties, delta, propertyName);
                }
            }
            else
            {
                HashSet<string> structuralPropertyNames = new HashSet<string>();

                foreach(IEdmStructuralProperty structuralProperty in expectedType.DeclaredStructuralProperties())
                {
                    structuralPropertyNames.Add(structuralProperty.Name);
                }

                foreach (PropertyInfo property in graph.GetType().GetProperties())
                {
                    if (structuralPropertyNames.Contains(property.Name))
                    {
                        object propertyValue = property.GetValue(graph);
                        IEdmStructuredTypeReference expectedPropType = null;

                        if (propertyValue == null)
                        {
                            expectedPropType = writeContext.GetEdmType(propertyValue, property.PropertyType) as IEdmStructuredTypeReference;
                        }

                        SetPropertyValue(writeContext, properties, expectedPropType, property.Name, propertyValue);
                    }
                }
            }            

            resourceValue.Properties = properties;

            return resourceValue;
        }

        private void SetDeltaPropertyValue(ODataSerializerContext writeContext, List<ODataProperty> properties, IDelta delta, string propertyName)
        {
            object propertyValue;

            if (delta.TryGetPropertyValue(propertyName, out propertyValue))
            {
                Type propertyType;
                IEdmStructuredTypeReference expectedPropType = null;

                if (propertyValue == null)
                {
                    // We need expected property type only if property value is null, else it will get from the value
                    if (delta.TryGetPropertyType(propertyName, out propertyType))
                    {
                        expectedPropType = writeContext.GetEdmType(propertyValue, propertyType) as IEdmStructuredTypeReference;
                    }
                }

                SetPropertyValue(writeContext, properties, expectedPropType, propertyName, propertyValue);
            }
        }

        private void SetPropertyValue(ODataSerializerContext writeContext, List<ODataProperty> properties, IEdmStructuredTypeReference expectedType, string propertyName, object propertyValue)
        {
            if (propertyValue == null && expectedType == null)
            {
                properties.Add(new ODataProperty { Name = propertyName, Value = new ODataNullValue() });
            }
            else
            {
                IEdmTypeReference edmTypeReference;
                ODataEdmTypeSerializer edmTypeSerializer = null;

                edmTypeReference = propertyValue == null ? expectedType : writeContext.GetEdmType(propertyValue,
                    propertyValue.GetType());

                if (edmTypeReference != null)
                {
                    edmTypeSerializer = GetResourceValueEdmTypeSerializer(edmTypeReference);
                }

                if (edmTypeSerializer != null)
                {
                    ODataValue odataValue = edmTypeSerializer.CreateODataValue(propertyValue, edmTypeReference, writeContext);
                    properties.Add(new ODataProperty { Name = propertyName, Value = odataValue });
                }
            }
        }

        private ODataEdmTypeSerializer GetResourceValueEdmTypeSerializer(IEdmTypeReference edmTypeReference)
        {
            ODataEdmTypeSerializer edmTypeSerializer;

            if (edmTypeReference.IsCollection())
            {
                edmTypeSerializer = new ODataCollectionSerializer(SerializerProvider, true);
            }
            else if (edmTypeReference.IsStructured())
            {
                edmTypeSerializer = new ODataResourceValueSerializer(SerializerProvider);
            }
            else
            {
                edmTypeSerializer = SerializerProvider.GetEdmTypeSerializer(edmTypeReference);
            }

            return edmTypeSerializer;
        }
    }
}
