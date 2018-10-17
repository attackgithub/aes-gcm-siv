using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Cryptography.Tests
{
	public class AesGcmSivTest
	{
		private const string Aes128GcmSiv = "Vectors/aes-128-gcm-siv.json";
		private const string Aes256GcmSiv = "Vectors/aes-256-gcm-siv.json";
		private const string Authentication1000 = "Vectors/authentication-1000.json";
		private const string CounterWrap = "Vectors/counter-wrap.json";
		private const string Encryption1000 = "Vectors/encryption-1000.json";

		private static readonly FieldInfo threshold =
			typeof(AesGcmSiv).GetField("threshold", BindingFlags.Instance | BindingFlags.NonPublic);

		[Fact]
		public void TestEncrypt()
		{
			var files = new string[]
			{
				Aes256GcmSiv,
				Authentication1000,
				CounterWrap,
				Encryption1000
			};

			foreach (var vector in files.SelectMany(LoadVectors))
			{
				using (var siv = new AesGcmSiv(vector.Key))
				{
					TestEncryptSingle(siv, vector);

					threshold.SetValue(siv, -1);
					TestEncryptSingle(siv, vector);

					threshold.SetValue(siv, Int32.MaxValue);
					TestEncryptSingle(siv, vector);
				}
			}
		}

		private void TestEncryptSingle(AesGcmSiv siv, Vector vector)
		{
			var tag = new byte[16];
			var ciphertext = new byte[vector.Plaintext.Length];

			siv.Encrypt(vector.Nonce, vector.Plaintext, ciphertext, tag, vector.Aad);
			Assert.Equal(Hex.Encode(vector.Result), Hex.Encode(Concat(ciphertext, tag)));

			siv.Encrypt((ReadOnlySpan<byte>)vector.Nonce, vector.Plaintext, ciphertext, tag, vector.Aad);
			Assert.Equal(Hex.Encode(vector.Result), Hex.Encode(Concat(ciphertext, tag)));
		}

		[Fact]
		public void TestDecrypt()
		{
			var files = new string[]
			{
				Aes256GcmSiv,
				Authentication1000,
				CounterWrap,
				Encryption1000
			};

			foreach (var vector in files.SelectMany(LoadVectors))
			{
				using (var siv = new AesGcmSiv(vector.Key))
				{
					TestDecryptSingle(siv, vector);

					threshold.SetValue(siv, -1);
					TestDecryptSingle(siv, vector);

					threshold.SetValue(siv, Int32.MaxValue);
					TestDecryptSingle(siv, vector);
				}
			}
		}

		private void TestDecryptSingle(AesGcmSiv siv, Vector vector)
		{
			var ciphertext = new byte[vector.Plaintext.Length];
			var tag = new byte[16];

			Array.Copy(vector.Result, ciphertext, vector.Plaintext.Length);
			Array.Copy(vector.Result, vector.Plaintext.Length, tag, 0, tag.Length);

			siv.Decrypt(vector.Nonce, ciphertext, tag, ciphertext, vector.Aad);
			Assert.Equal(Hex.Encode(vector.Plaintext), Hex.Encode(ciphertext));

			Array.Copy(vector.Result, ciphertext, vector.Plaintext.Length);
			Array.Copy(vector.Result, vector.Plaintext.Length, tag, 0, tag.Length);

			siv.Decrypt((ReadOnlySpan<byte>)vector.Nonce, ciphertext, tag, ciphertext, vector.Aad);
			Assert.Equal(Hex.Encode(vector.Plaintext), Hex.Encode(ciphertext));
		}

		[Fact(Skip = "Takes too long to complete.")]
		public void TestMaxInputLengthManaged()
		{
			var key = new byte[32];
			var nonce = new byte[12];
			var plaintext = new byte[0x7fffffc7];
			var tag = new byte[16];
			var empty = new byte[0];

			using (var siv = new AesGcmSiv(key))
			{
				siv.Encrypt(nonce, plaintext, plaintext, tag);
				Assert.Equal("b8f9d292c80c757ce0639ee04dba3ebd", Hex.Encode(tag));

				Array.Clear(plaintext, 0, plaintext.Length);

				siv.Encrypt(nonce, empty, empty, tag, plaintext);
				Assert.Equal("a6126fd232ed46bfa639cef6418b14fd", Hex.Encode(tag));

				Array.Clear(plaintext, 0, plaintext.Length);

				siv.Encrypt(nonce, plaintext, plaintext, tag, plaintext);
				Assert.Equal("6d15c063e7c3d68db84201d887ddde46", Hex.Encode(tag));
			}
		}

		[Fact(Skip = "Takes too long to complete.")]
		public unsafe void TestMaxInputLengthUnmanaged()
		{
			var length = Int32.MaxValue;
			var buffer = Marshal.AllocHGlobal(length);

			try
			{
				var key = new byte[32];
				var nonce = new byte[12];
				var plaintext = new Span<byte>(buffer.ToPointer(), length);
				var tag = new byte[16];

				using (var siv = new AesGcmSiv(key))
				{
					siv.Encrypt(nonce, plaintext, plaintext, tag, default);
					Assert.Equal("b8246fbcb073f59dbf963b46a19db688", Hex.Encode(tag));

					plaintext.Clear();

					siv.Encrypt(nonce, default, default, tag, plaintext);
					Assert.Equal("c5b65922f2f64799a1d62c8036520c9d", Hex.Encode(tag));

					plaintext.Clear();

					siv.Encrypt(nonce, plaintext, plaintext, tag, plaintext);
					Assert.Equal("e017665be4c97b25610602e6e4c81a5e", Hex.Encode(tag));
				}
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}

		private static IEnumerable<Vector> LoadVectors(string file)
		{
			var s = File.ReadAllText(file);
			var json = JObject.Parse(s);

			foreach (var vector in json["vectors"])
			{
				yield return new Vector
				{
					Plaintext = GetBytes(vector, "plaintext"),
					Aad = GetBytes(vector, "aad"),
					Key = GetBytes(vector, "key"),
					Nonce = GetBytes(vector, "nonce"),
					RecordAuthenticationKey = GetBytes(vector, "record_authentication_key"),
					RecordEncryptionKey = GetBytes(vector, "record_encryption_key"),
					PolyvalInput = GetBytes(vector, "polyval_input"),
					PolyvalResult = GetBytes(vector, "polyval_result"),
					PolyvalResultXorNonce = GetBytes(vector, "polyval_result_xor_nonce"),
					PolyvalResultXorNonceMasked = GetBytes(vector, "polyval_result_xor_nonce_masked"),
					Tag = GetBytes(vector, "tag"),
					InitialCounter = GetBytes(vector, "initial_counter"),
					Result = GetBytes(vector, "result")
				};
			}
		}

		private static string GetString(JToken token, string property)
		{
			return (string)token[property] ?? String.Empty;
		}

		private static byte[] GetBytes(JToken token, string property)
		{
			return Hex.Decode(GetString(token, property));
		}

		private static byte[] Concat(byte[] x, byte[] y)
		{
			var result = new byte[x.Length + y.Length];

			x.CopyTo(result, 0);
			y.CopyTo(result, x.Length);

			return result;
		}
	}
}
