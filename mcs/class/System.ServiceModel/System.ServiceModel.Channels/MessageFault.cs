//
// MessageFault.cs
//
// Author:
//	Atsushi Enomoto <atsushi@ximian.com>
//
// Copyright (C) 2005-2009 Novell, Inc.  http://www.novell.com
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace System.ServiceModel.Channels
{
	public abstract class MessageFault
	{
		// type members

		public static MessageFault CreateFault (Message message, int maxBufferSize)
		{
			try {
				if (message.Version.Envelope == EnvelopeVersion.Soap11)
					return CreateFault11 (message, maxBufferSize);
				else // common to None and SOAP12
					return CreateFault (message, maxBufferSize, message.Version.Envelope.Namespace);
			} catch (XmlException ex) {
				throw new CommunicationException ("Received an invalid SOAP Fault message", ex);
			}
			throw new InvalidOperationException ("The input message is not a SOAP envelope.");
		}

		static MessageFault CreateFault11 (Message message, int maxBufferSize)
		{
			FaultCode fc = null;
			FaultReason fr = null;
			object details = null;
			XmlDictionaryReader r = message.GetReaderAtBodyContents ();
			r.ReadStartElement ("Fault", message.Version.Envelope.Namespace);
			r.MoveToContent ();

			while (r.NodeType != XmlNodeType.EndElement) {
				switch (r.LocalName) {
				case "faultcode":
					fc = ReadFaultCode11 (r);
					break;
				case "faultstring":
					fr = new FaultReason (r.ReadElementContentAsString());
					break;
				case "detail":
					//BUGBUG: Handle children of type other than ExceptionDetail, in order to comply with 
					//        FaultContractAttribute.
					r.ReadStartElement ();
					r.MoveToContent();
					details = new DataContractSerializer (typeof (ExceptionDetail)).ReadObject (r);
					break;
				case "faultactor":
				default:
					throw new NotImplementedException ();
				}
				r.MoveToContent ();
			}
			r.ReadEndElement ();

			if (fr == null)
				throw new XmlException ("Reason is missing in the Fault message");

			if (details == null)
				return CreateFault (fc, fr);
			return CreateFault (fc, fr, details);
		}

		static MessageFault CreateFault (Message message, int maxBufferSize, string ns)
		{
			FaultCode fc = null;
			FaultReason fr = null;
			XmlDictionaryReader r = message.GetReaderAtBodyContents ();
			r.ReadStartElement ("Fault", ns);
			r.MoveToContent ();

			while (r.NodeType != XmlNodeType.EndElement) {
				switch (r.LocalName) {
				case "Code":
					fc = ReadFaultCode (r, ns);
					break;
				case "Reason":
					fr = ReadFaultReason (r, ns);
					break;
				default:
					throw new XmlException (String.Format ("Unexpected node {0} name {1}", r.NodeType, r.Name));
				}
				r.MoveToContent ();
			}

			if (fr == null)
				throw new XmlException ("Reason is missing in the Fault message");

			r.ReadEndElement ();

			return CreateFault (fc, fr);
		}

		static FaultCode ReadFaultCode11 (XmlDictionaryReader r)
		{
			FaultCode subcode = null;
			XmlQualifiedName value = XmlQualifiedName.Empty;

			if (r.IsEmptyElement)
				throw new ArgumentException ("Fault Code is mandatory in SOAP fault message.");

			r.ReadStartElement ("faultcode");
			r.MoveToContent ();
			while (r.NodeType != XmlNodeType.EndElement) {
				if (r.NodeType == XmlNodeType.Element)
					subcode = ReadFaultCode11 (r);
				else
					value = (XmlQualifiedName) r.ReadContentAs (typeof (XmlQualifiedName), r as IXmlNamespaceResolver);
				r.MoveToContent ();
			}
			r.ReadEndElement ();

			return new FaultCode (value.Name, value.Namespace, subcode);
		}

		static FaultCode ReadFaultCode (XmlDictionaryReader r, string ns)
		{
			FaultCode subcode = null;
			XmlQualifiedName value = XmlQualifiedName.Empty;

			if (r.IsEmptyElement)
				throw new ArgumentException ("either SubCode or Value element is mandatory in SOAP fault code.");

			r.ReadStartElement (); // could be either Code or SubCode
			r.MoveToContent ();
			while (r.NodeType != XmlNodeType.EndElement) {
				switch (r.LocalName) {
				case "Subcode":
					subcode = ReadFaultCode (r, ns);
					break;
				case "Value":
					value = (XmlQualifiedName) r.ReadElementContentAs (typeof (XmlQualifiedName), r as IXmlNamespaceResolver, "Value", ns);
					break;
				default:
					throw new ArgumentException ();
				}
				r.MoveToContent ();
			}
			r.ReadEndElement ();

			return new FaultCode (value.Name, value.Namespace, subcode);
		}

		static FaultReason ReadFaultReason (XmlDictionaryReader r, string ns)
		{
			List<FaultReasonText> l = new List<FaultReasonText> ();
			if (r.IsEmptyElement)
				throw new ArgumentException ("One or more Text element is mandatory in SOAP fault reason text.");

			r.ReadStartElement ("Reason", ns);
			for (r.MoveToContent ();
			     r.NodeType != XmlNodeType.EndElement;
			     r.MoveToContent ()) {
				string lang = r.GetAttribute ("lang", "http://www.w3.org/XML/1998/namespace");
				if (lang == null)
					throw new XmlException ("xml:lang is mandatory on fault reason Text");
				l.Add (new FaultReasonText (r.ReadElementContentAsString ("Text", ns), lang));
			}
			return new FaultReason (l);
		}

		public static MessageFault CreateFault (FaultCode code,
			string reason)
		{
			return CreateFault (code, new FaultReason (reason));
		}

		public static MessageFault CreateFault (FaultCode code,
			FaultReason reason)
		{
			return new SimpleMessageFault (code, reason,
				 false, null, null, null, null);
		}

		public static MessageFault CreateFault (FaultCode code,
			FaultReason reason, object detail)
		{
			return new SimpleMessageFault (code, reason,
				true, detail, new DataContractSerializer (detail.GetType ()), null, null);
		}

		public static MessageFault CreateFault (FaultCode code,
			FaultReason reason, object detail,
			XmlObjectSerializer formatter)
		{
			return new SimpleMessageFault (code, reason, true,
				detail, formatter, String.Empty, String.Empty);
		}

		public static MessageFault CreateFault (FaultCode code,
			FaultReason reason, object detail,
			XmlObjectSerializer formatter, string actor)
		{
			return new SimpleMessageFault (code, reason,
				true, detail, formatter, actor, String.Empty);
		}

		public static MessageFault CreateFault (FaultCode code,
			FaultReason reason, object detail,
			XmlObjectSerializer formatter, string actor, string node)
		{
			return new SimpleMessageFault (code, reason,
				true, detail, formatter, actor, node);
		}

		// pretty simple implementation class
		internal class SimpleMessageFault : MessageFault
		{
			bool has_detail;
			string actor, node;
			FaultCode code;
			FaultReason reason;
			object detail;
			XmlObjectSerializer formatter;

			public SimpleMessageFault (FaultCode code,
				FaultReason reason, bool has_detail,
				object detail, XmlObjectSerializer formatter,
				string actor, string node)
				: this (code, reason, detail, formatter, actor, node)
			{
				this.has_detail = has_detail;
			}

			public SimpleMessageFault (FaultCode code,
				FaultReason reason,
				object detail, XmlObjectSerializer formatter,
				string actor, string node)
			{
				if (code == null)
					throw new ArgumentNullException ("code");
				if (reason == null)
					throw new ArgumentNullException ("reason");

				this.code = code;
				this.reason = reason;
				this.detail = detail;
				this.formatter = formatter;
				this.actor = actor;
				this.node = node;
			}

			public override string Actor {
				get { return actor; }
			}

			public override FaultCode Code {
				get { return code; }
			}

			public override bool HasDetail {
				// it is not simply "detail != null" since
				// null detail could become <ms:anyType xsi:nil="true" />
				get { return has_detail; }
			}

			public override string Node {
				get { return node; }
			}

			public override FaultReason Reason {
				get { return reason; }
			}

			protected override XmlDictionaryReader OnGetReaderAtDetailContents ()
			{
				// FIXME: use XmlObjectSerializer
				return base.OnGetReaderAtDetailContents ();
			}

			protected override void OnWriteDetailContents (XmlDictionaryWriter writer)
			{
				formatter.WriteObject (writer, detail);
			}

			public object Detail {
				get { return detail; }
			}
		}

		// instance members

		protected MessageFault ()
		{
		}

		[MonoTODO ("is this true?")]
		public virtual string Actor {
			get { return String.Empty; }
		}

		public abstract FaultCode Code { get; }

		public abstract bool HasDetail { get; }

		[MonoTODO ("is this true?")]
		public virtual string Node {
			get { return String.Empty; }
		}

		public abstract FaultReason Reason { get; }

		public T GetDetail<T> ()
		{
			return GetDetail<T> (new DataContractSerializer (typeof (T)));
		}

		public T GetDetail<T> (XmlObjectSerializer formatter)
		{
			if (!HasDetail)
				throw new InvalidOperationException ("This message does not have details.");

			return (T) formatter.ReadObject (GetReaderAtDetailContents ());
		}

		public XmlDictionaryReader GetReaderAtDetailContents ()
		{
			return OnGetReaderAtDetailContents ();
		}

		public void WriteTo (XmlDictionaryWriter writer,
			EnvelopeVersion version)
		{
			writer.WriteStartElement ("Fault", version.Namespace);
			WriteFaultCode (writer, version, Code);
			WriteReason (writer, version);
			if (HasDetail)
				OnWriteDetail (writer, version);
			writer.WriteEndElement ();
		}

		private void WriteFaultCode (XmlDictionaryWriter writer, 
			EnvelopeVersion version, FaultCode code)
		{
			if (version == EnvelopeVersion.Soap11) {
				writer.WriteStartElement ("", "faultcode", version.Namespace);
				if (code.Namespace.Length > 0)
					writer.WriteXmlnsAttribute ("a", code.Namespace);
				writer.WriteQualifiedName (code.Name, code.Namespace);
				writer.WriteEndElement ();
			} else { // Soap12
				writer.WriteStartElement ("Code", version.Namespace);
				writer.WriteStartElement ("Value", version.Namespace);
				if (code.Namespace.Length > 0)
					writer.WriteXmlnsAttribute ("a", code.Namespace);
				writer.WriteQualifiedName (code.Name, code.Namespace);
				if (code.SubCode != null)
					WriteFaultCode (writer, version, code.SubCode);
				writer.WriteEndElement ();
				writer.WriteEndElement ();
			}
		}

		private void WriteReason (XmlDictionaryWriter writer, 
			EnvelopeVersion version)
		{
			if (version == EnvelopeVersion.Soap11) {
				foreach (FaultReasonText t in Reason.Translations) {
					writer.WriteStartElement ("", "faultstring", version.Namespace);
					if (t.XmlLang != null)
						writer.WriteAttributeString ("xml", "lang", null, t.XmlLang);
					writer.WriteString (t.Text);
					writer.WriteEndElement ();
				}
			} else { // Soap12
				writer.WriteStartElement ("Reason", version.Namespace);
				foreach (FaultReasonText t in Reason.Translations) {
					writer.WriteStartElement ("Text", version.Namespace);
					if (t.XmlLang != null)
						writer.WriteAttributeString ("xml", "lang", null, t.XmlLang);
					writer.WriteString (t.Text);
					writer.WriteEndElement ();
				}
				writer.WriteEndElement ();
			}
		}

		public void WriteTo (XmlWriter writer, EnvelopeVersion version)
		{
			WriteTo (XmlDictionaryWriter.CreateDictionaryWriter (
				writer), version);
		}

		protected virtual XmlDictionaryReader OnGetReaderAtDetailContents ()
		{
			MemoryStream ms = new MemoryStream ();
			using (XmlDictionaryWriter dw =
				XmlDictionaryWriter.CreateDictionaryWriter (
					XmlWriter.Create (ms))) {
				OnWriteDetailContents (dw);
			}
			ms.Seek (0, SeekOrigin.Begin);
			return XmlDictionaryReader.CreateDictionaryReader (
				XmlReader.Create (ms));
		}

		protected virtual void OnWriteDetail (XmlDictionaryWriter writer, EnvelopeVersion version)
		{
			OnWriteStartDetail (writer, version);
			OnWriteDetailContents (writer);
			writer.WriteEndElement ();
		}

		protected virtual void OnWriteStartDetail (XmlDictionaryWriter writer, EnvelopeVersion version)
		{
			if (version == EnvelopeVersion.Soap11)
				writer.WriteStartElement ("detail", String.Empty);
			else // Soap12
				writer.WriteStartElement ("Detail", version.Namespace);
		}

		protected abstract void OnWriteDetailContents (XmlDictionaryWriter writer);
	}
}
