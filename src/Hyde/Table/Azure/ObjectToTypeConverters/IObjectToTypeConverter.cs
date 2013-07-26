using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Microsoft.WindowsAzure.Storage.Table;

namespace TechSmith.Hyde.Table.Azure.ObjectToTypeConverters
{
   internal interface IObjectToTypeConverter
   {
      object ConvertToValue( EntityProperty entityProperty, PropertyInfo propertyInfo );
      bool CanConvertType( Type type );
      IEnumerable<Type> GetSupportedTypes();
      EntityProperty ConvertToEntityProperty( object rawItem, Type rawItemType );
   }

   internal abstract class ReferenceTypeConverter<T> : IObjectToTypeConverter
   {
      private readonly Func<EntityProperty, T> _toValue;
      private readonly Func<object, EntityProperty> _toEntityProperty;

      protected ReferenceTypeConverter( Func<EntityProperty, T> toValue, Func<object, EntityProperty> toEntityProperty )
      {
         _toValue = toValue;
         _toEntityProperty = toEntityProperty;
      }

      public virtual object ConvertToValue( EntityProperty entityProperty, PropertyInfo propertyInfo )
      {
         Type typeToConvertTo = propertyInfo.PropertyType;

         EnsureTypeIsConvertable( typeToConvertTo );
         return _toValue( entityProperty );
      }

      protected void EnsureTypeIsConvertable( Type type )
      {
         if ( !CanConvertType( type ) )
         {
            throw new NotSupportedException( string.Format( "The type {0} is not supported by {1}", type.Name, GetType().Name ) );
         }
      }

      public virtual bool CanConvertType( Type type )
      {
         return typeof( T ) == type;
      }

      public virtual IEnumerable<Type> GetSupportedTypes()
      {
         return new List<Type> { typeof( T ) };
      }

      public virtual EntityProperty ConvertToEntityProperty( object rawItem, Type rawItemType )
      {
         EnsureTypeIsConvertable( rawItemType );
         return _toEntityProperty( rawItem );
      }
   }

   internal abstract class ValueTypeConverter<T> : IObjectToTypeConverter where T : struct
   {
      private readonly Func<EntityProperty, T?> _toValue;
      private readonly Func<object, EntityProperty> _toEntityProperty;

      protected ValueTypeConverter( Func<EntityProperty, T?> toValue, Func<object, EntityProperty> toEntityProperty )
      {
         _toValue = toValue;
         _toEntityProperty = toEntityProperty;
      }

      public virtual object ConvertToValue( EntityProperty entityProperty, PropertyInfo propertyInfo )
      {
         Type typeToConvertTo = propertyInfo.PropertyType;

         EnsureTypeIsConvertable( typeToConvertTo );
         if ( typeToConvertTo == typeof( T? ) )
         {
            return _toValue( entityProperty );
         }
         return _toValue( entityProperty ).Value;
      }

      protected void EnsureTypeIsConvertable( Type type )
      {
         if ( !CanConvertType( type ) )
         {
            throw new NotSupportedException( string.Format( "The type {0} is not supported by {1}", type.Name, GetType().Name ) );
         }
      }

      public virtual bool CanConvertType( Type type )
      {
         return typeof( T ) == type || typeof( T? ) == type;
      }

      public virtual IEnumerable<Type> GetSupportedTypes()
      {
         return new List<Type> { typeof( T ), typeof( T? ) };
      }

      public virtual EntityProperty ConvertToEntityProperty( object rawItem, Type rawItemType )
      {
         EnsureTypeIsConvertable( rawItemType );
         return _toEntityProperty( rawItem );
      }
   }

   internal class BoolTypeConverter : ValueTypeConverter<bool>
   {
      public BoolTypeConverter()
         : base( ep => ep.BooleanValue, o => new EntityProperty( (bool?) o ) )
      {
      }
   }

   internal class ByteArrayConverter : ReferenceTypeConverter<byte[]>
   {
      public ByteArrayConverter()
         : base( ep => ep.BinaryValue, o => new EntityProperty( (byte[]) o ) )
      {
      }
   }

   internal class DateTimeConverter : ValueTypeConverter<DateTime>
   {      
      // The minimum datetime value allowable by Table Storage.
      // "A 64-bit value expressed as Coordinated Universal Time (UTC). The supported DateTime range begins from 12:00 midnight, January 1, 1601 A.D. (C.E.), UTC. The range ends at December 31, 9999."
      // source: http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
      internal static readonly DateTime MinimumSupportedDateTime = new DateTime( year: 1601, month: 1, day: 1, hour: 0, minute: 0, second: 0, kind: DateTimeKind.Utc );
      public DateTimeConverter()
         : base( 
         ep =>
         {
            try
            {
               if ( ep.DateTimeOffsetValue.HasValue )
               {
                  return ep.DateTimeOffsetValue.Value.UtcDateTime;
               }
            }
            catch ( InvalidOperationException )
            {
               DateTimeOffset fromString;
               DateTimeOffset.TryParse( ep.StringValue, out fromString );
               return fromString.UtcDateTime;
            }
            return null;
         }, 
         o =>
         {
            var date = (DateTime?) o;
            DateTimeOffset? value = null;
            if ( date.HasValue )
            {
               if( date.Value.Kind == DateTimeKind.Unspecified )
               {
                  date = new DateTime( date.Value.Ticks, DateTimeKind.Utc );
               }

               value = new DateTimeOffset( date.Value );
               // For dates that table storage cannot support with an Edm type, we store them as a string
               if ( date.Value < MinimumSupportedDateTime )
               {
                  var stringValue = value.Value.ToString();
                  return new EntityProperty( stringValue );
               }
           }

            return new EntityProperty( value );
         } )
      {
      }
   }

   internal class DateTimeOffsetConverter : ValueTypeConverter<DateTimeOffset>
   {
      // The minimum datetime value allowable by Table Storage.
      // "A 64-bit value expressed as Coordinated Universal Time (UTC). The supported DateTime range begins from 12:00 midnight, January 1, 1601 A.D. (C.E.), UTC. The range ends at December 31, 9999."
      // source: http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
      internal static readonly DateTimeOffset MinimumSupportedDateTimeOffset = new DateTimeOffset( year: 1601, month: 1, day: 1, hour: 0, minute: 0, second: 0, offset: TimeSpan.FromTicks( 0 ) );

      public DateTimeOffsetConverter()
         : base( ep => ep.DateTimeOffsetValue, o =>
         {
            var dateTimeOffset = (DateTimeOffset?) o;
            if ( dateTimeOffset.HasValue )
            {
               if ( dateTimeOffset.Value < MinimumSupportedDateTimeOffset )
               {
                  throw new ArgumentOutOfRangeException( "Object contains a DateTimeOffset value that falls below the range supported by Table Storage." );
               }
            }
            return new EntityProperty( dateTimeOffset );
         } )
      {
      }
   }

   internal class DoubleConverter : ValueTypeConverter<double>
   {
      public DoubleConverter()
         : base( ep => ep.DoubleValue, o => new EntityProperty( (double?) o ) )
      {
      }
   }

   internal class GuidConverter : ValueTypeConverter<Guid>
   {
      public GuidConverter()
         : base( ep => ep.GuidValue, o => new EntityProperty( (Guid?) o ) )
      {
      }
   }

   internal class IntegerConverter : ValueTypeConverter<int>
   {
      public IntegerConverter()
         : base( ep => ep.Int32Value, o => new EntityProperty( (int?) o ) )
      {
      }
   }

   internal class LongConverter : ValueTypeConverter<long>
   {
      public LongConverter()
         : base( ep => ep.Int64Value, o => new EntityProperty( (long?) o ) )
      {
      }
   }

   internal class StringConverter : ReferenceTypeConverter<string>
   {
      public StringConverter()
         : base( ep => ep.StringValue, o => new EntityProperty( (string) o ) )
      {
      }
   }

   internal class UriConverter : ReferenceTypeConverter<Uri>
   {
      public UriConverter()
         : base(
             ep => string.IsNullOrWhiteSpace( ep.StringValue ) ? null : new Uri( ep.StringValue),
             o => o == null ? new EntityProperty( (string) null ) : new EntityProperty( ( (Uri) o ).AbsoluteUri ) )
      {
      }
   }

   internal class EnumConverter : IntegerConverter
   {
      public override object ConvertToValue( EntityProperty entityProperty, PropertyInfo propertyInfo )
      {
         Type typeToConvertTo = propertyInfo.PropertyType;
         EnsureTypeIsConvertable( typeToConvertTo );

         if ( entityProperty.PropertyType != EdmType.Int32 )
         {
            throw new InvalidOperationException( string.Format( "Cannot convert {0} to an Enum for property {1}", entityProperty.PropertyType, propertyInfo.Name ) );
         }

         int intValue = (int) base.ConvertToValue( entityProperty, propertyInfo );

         if ( Enum.IsDefined( typeToConvertTo, intValue ) )
         {
            return Enum.ToObject( typeToConvertTo, intValue );
         }

         Array enumValues = Enum.GetValues( typeToConvertTo );
         if ( enumValues.Length > 0 )
         {
            return enumValues.GetValue( 0 );
         }

         return 0;
      }

      public override bool CanConvertType( Type type )
      {
         return type.IsEnum;
      }

      public override IEnumerable<Type> GetSupportedTypes()
      {
         return new List<Type> { typeof( Enum ) };
      }

      public override EntityProperty ConvertToEntityProperty( object rawItem, Type rawItemType )
      {
         EnsureTypeIsConvertable( rawItemType );

         return new EntityProperty( (int) rawItem );
      }
   }
}