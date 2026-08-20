namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// Hashing, symmetric encryption and cryptographically secure random numbers. Scripts reach it through
		/// the KS module: <c>#Import "Ks" { Crypt }</c>, then <c>Crypt.SHA256("text")</c>.
		/// <para>
		/// A String is taken as its UTF-8 bytes, which is what every other tool means by the hash of a text;
		/// pass <c>Encoding</c> to choose a different one. A Buffer or an Array of bytes is used as it stands,
		/// so no encoding applies to those, and anything that hashes also accepts an open File.</para>
		/// <para>
		/// A digest is returned as uppercase hexadecimal. Compare digests case-insensitively — the tool a
		/// checksum came from may well print it in lowercase.</para>
		/// <para>
		/// The per-algorithm methods are a closed set carried over from the global functions they replaced. An
		/// algorithm added later is a name <see cref="Hash"/> accepts, not another method.</para>
		/// </summary>
		public class Crypt : KeysharpObject
		{
			/// <summary>
			/// Calculates the CRC32 polynomial of a value.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">The value to check, as <see cref="Hash"/> takes it.</param>
			/// <param name="encoding">The encoding a string <paramref name="value"/> is taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>The checksum as an integer, where <see cref="Hash"/> returns it as hexadecimal.</returns>
			/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
			/// <exception cref="TypeError">Thrown if the value holds no bytes.</exception>
			[Static]
			public static object CRC32(object @this, object value, object encoding = null)
			{
				var digest = Hash(@this, value, "CRC32", encoding);
				return digest is string hex && hex.Length != 0 ? Convert.ToInt64(hex, 16) : 0L;
			}

			/// <summary>
			/// Decrypts data produced by <see cref="Encrypt"/>.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">The encrypted data, normally the <see cref="Buffer"/> Encrypt returned.</param>
			/// <param name="key">The secret key it was encrypted under.</param>
			/// <param name="algorithm">The cipher, as for <see cref="Encrypt"/>. Must match what encrypted it.</param>
			/// <param name="mode">The chaining mode, as for <see cref="Encrypt"/>. Must match what encrypted it.</param>
			/// <param name="iv">Omit it for data <see cref="Encrypt"/> produced, whose initialization vector is
			/// read from the front of it. Supply the same vector Encrypt was given if one was, in which case the
			/// whole of <paramref name="value"/> is taken as ciphertext.</param>
			/// <param name="encoding">The encoding a string <paramref name="key"/> is taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8, and must match what Encrypt was given.</param>
			/// <returns>The decrypted data, as a <see cref="Buffer"/>.</returns>
			/// <exception cref="ValueError">Thrown if the cipher, mode, vector or encoding cannot be resolved, or
			/// if the data cannot be decrypted with the key, mode and algorithm supplied.</exception>
			/// <exception cref="TypeError">Thrown if the value or key holds no bytes.</exception>
			[Static]
			public static object Decrypt(object @this, object value, object key, object algorithm = null, object mode = null, object iv = null, object encoding = null)
				=> Cipher(value, key, true, algorithm, mode, iv, encoding);

			/// <summary>
			/// Encrypts data with a symmetric cipher.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">The data to encrypt.</param>
			/// <param name="key">The secret key, padded to the algorithm's key size. It is used as it stands, so
			/// a passphrase is only as strong as its own entropy.</param>
			/// <param name="algorithm">The cipher, defaulting to AES.</param>
			/// <param name="mode">GCM, which authenticates as well as encrypts, or one of the chaining modes CBC
			/// (the default), ECB and CFB, which do not.</param>
			/// <param name="iv">The initialization vector, or the nonce under GCM: 16 bytes for a chaining mode
			/// and 12 for GCM, as <see cref="RandomBytes"/> returns them. If omitted, a random one is drawn per
			/// call and written in front of the result, where <see cref="Decrypt"/> finds it. Supply one only to
			/// match a format something else defined: it is then used as it stands and is not written to the
			/// result. ECB uses none and accepts none.</param>
			/// <param name="encoding">The encoding a string <paramref name="value"/>, <paramref name="key"/> or
			/// <paramref name="iv"/> is taken in, named as for <see cref="A_FileEncoding"/>. Defaults to UTF-8.
			/// It decides how many bytes a string vector comes to, so it is what makes one the right length.</param>
			/// <returns>The encrypted data, as a <see cref="Buffer"/>.</returns>
			/// <exception cref="ValueError">Thrown if the cipher, mode, vector or encoding cannot be resolved.</exception>
			/// <exception cref="TypeError">Thrown if the value or key holds no bytes.</exception>
			/// <remarks>Encrypting the same text twice gives different results, because each call draws its own
			/// vector. Under a chaining mode the result carries no authentication tag, so a modified ciphertext
			/// decrypts to rubbish rather than being detected; under GCM it carries one and is detected.</remarks>
			[Static]
			public static object Encrypt(object @this, object value, object key, object algorithm = null, object mode = null, object iv = null, object encoding = null)
				=> Cipher(value, key, false, algorithm, mode, iv, encoding);

			/// <summary>
			/// Calculates the hash of a value with any of the supported algorithms.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">The value to hash: a String, a <see cref="Buffer"/>, an <see cref="Array"/> of
			/// bytes, or an open <see cref="KeysharpFile"/>, whose whole content is read as a stream and whose
			/// position is left where it was.</param>
			/// <param name="algorithm">One of MD5, SHA1, SHA256 (the default), SHA384, SHA512 or CRC32.</param>
			/// <param name="encoding">The encoding a string <paramref name="value"/> is taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>The digest as uppercase hexadecimal.</returns>
			/// <exception cref="ValueError">Thrown if the algorithm or the encoding cannot be resolved, or if the
			/// File is not open for reading.</exception>
			/// <exception cref="TypeError">Thrown if the value holds no bytes.</exception>
			[Static]
			public static object Hash(object @this, object value, object algorithm = null, object encoding = null)
			{
				var name = Named(algorithm, DefaultAlgorithm);
				var alg = CreateAlgorithm(name);

				if (alg == null)
					return UnknownAlgorithm(name);

				using (alg)
					return Digest(value, alg, ResolveEncoding(encoding));
			}

			/// <summary>
			/// Calculates the hash of a file's content, reading it as a stream so that its size does not matter.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="path">The file to hash, assumed to be in <see cref="A_WorkingDir"/> if an absolute
			/// path is not specified.</param>
			/// <param name="algorithm">One of MD5, SHA1, SHA256 (the default), SHA384, SHA512 or CRC32.</param>
			/// <returns>The digest as uppercase hexadecimal.</returns>
			/// <exception cref="ValueError">Thrown if no file name was supplied, or if the algorithm is not one
			/// of those named.</exception>
			/// <exception cref="OSError">Thrown if the file cannot be read.</exception>
			[Static]
			public static object HashFile(object @this, object path, object algorithm = null)
			{
				var file = path.As();
				var name = Named(algorithm, DefaultAlgorithm);
				var alg = CreateAlgorithm(name);

				if (alg == null)
					return UnknownAlgorithm(name);

				using (alg)
				{
					if (file.Length == 0)
						return Errors.ValueErrorOccurred("Crypt.HashFile requires a file name.", null, "");

					_ = Script.TheScript.Permissions.EnsureFileAccess(file, FilePermissionAccess.Read, operation: "Crypt.HashFile");
					ThreadAccessors.A_LastError = 0;

					try
					{
						using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
						return HexDigest(stream, alg);
					}
					catch (Exception ex)
					{
						ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
						return Errors.OSErrorOccurred(ex, $"Error reading file {file}", "");
					}
				}
			}

			/// <summary>
			/// Calculates the MD5 hash of a value.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">The value to hash, as <see cref="Hash"/> takes it.</param>
			/// <param name="encoding">The encoding a string <paramref name="value"/> is taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>A 32-character hexadecimal number.</returns>
			/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
			/// <exception cref="TypeError">Thrown if the value holds no bytes.</exception>
			/// <remarks>MD5 is unsuitable for anything that must resist a deliberate collision.</remarks>
			[Static]
			public static object MD5(object @this, object value, object encoding = null) => Hash(@this, value, "MD5", encoding);

			/// <summary>
			/// Derives key material from a password, by iterating a hash over it enough times to make guessing
			/// the password expensive.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="password">The password to stretch.</param>
			/// <param name="salt">The salt: bytes which need not be secret, but which must differ per password
			/// so that two people choosing the same one do not derive the same key. <see cref="RandomBytes"/>
			/// makes them, and it must be stored alongside whatever the key protects.</param>
			/// <param name="iterations">How many times to iterate, 600000 by default. Raising it raises the cost
			/// of every guess an attacker makes, and of every derivation this script does.</param>
			/// <param name="length">How many bytes to produce, 32 by default, which is an AES-256 key.</param>
			/// <param name="algorithm">The hash to iterate: SHA1, SHA256 (the default), SHA384 or SHA512.</param>
			/// <param name="encoding">The encoding a string <paramref name="password"/> or <paramref name="salt"/>
			/// is taken in, named as for <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>The derived bytes, as a <see cref="Buffer"/>.</returns>
			/// <exception cref="ValueError">Thrown if the algorithm or encoding cannot be resolved, or if the
			/// iteration count or length is not positive.</exception>
			/// <exception cref="TypeError">Thrown if the password or salt holds no bytes.</exception>
			/// <remarks>This is PBKDF2. Deriving a key with it is what makes a passphrase usable as one, since
			/// <see cref="Encrypt"/> otherwise takes the key exactly as given.</remarks>
			[Static]
			public static object PBKDF2(object @this, object password, object salt, object iterations = null, object length = null, object algorithm = null, object encoding = null)
			{
				var rounds = iterations == null ? DefaultIterations : iterations.Al();
				var size = length == null ? DefaultKeyLength : length.Al();
				var derived = DeriveKey(password, salt, rounds, size, Named(algorithm, DefaultAlgorithm), ResolveEncoding(encoding));
				return derived == null ? "" : new Buffer(derived);
			}

			/// <summary>
			/// Returns cryptographically secure random bytes, for an initialization vector, a salt or a key.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="count">How many bytes to return.</param>
			/// <returns>The bytes, as a <see cref="Buffer"/>.</returns>
			/// <exception cref="ValueError">Thrown if the count is negative or larger than an array can hold.</exception>
			[Static]
			public static object RandomBytes(object @this, object count)
			{
				var n = count.Al();

				if (n < 0 || n > System.Array.MaxLength)
					return Errors.ValueErrorOccurred($"Crypt.RandomBytes requires a count between 0 and {System.Array.MaxLength}.", count, "");

				return new Buffer(RandomNumberGenerator.GetBytes((int)n));
			}

			/// <summary>
			/// Generates a secure (cryptographic) random number.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="min">The lower bound. If either parameter is a <see cref="double"/>, the result uses
			/// the floating-point path.</param>
			/// <param name="max">The upper bound, included in the range as <see cref="Random"/> includes it.</param>
			/// <returns>A random number between the specified range. Leave both parameters blank to allow the
			/// full numeric range. If <paramref name="min"/> and <paramref name="max"/> are both non-
			/// <see cref="double"/>, the result is an integer.</returns>
			/// <remarks>A cryptographic random number generator produces an output that is computationally
			/// infeasible to predict with a probability better than one half. <see cref="Random"/> uses a simpler
			/// algorithm which is much faster but less secure.</remarks>
			[Static]
			public static object SecureRandom(object @this, object min = null, object max = null)
			{
				if (min is double || max is double)
				{
					var minVal = min.Ad(double.MinValue);
					var maxVal = max.Ad(double.MaxValue);
					var diff = Math.Abs(minVal - maxVal);

					if (diff == 0 && !(minVal == 0 && maxVal == 0))
						return minVal;

					Span<byte> rnd = stackalloc byte[8];
					RandomNumberGenerator.Fill(rnd);
					var value = BitConverter.ToUInt64(rnd);
					var unit = value / (double)ulong.MaxValue;
					var rem = (minVal % 1.0) != 0 || (maxVal % 1.0) != 0;

					if (!rem)
					{
						var range = diff + 1.0;
						var val = Math.Floor(unit * range);
						return minVal + val;
					}

					return minVal + (unit * diff);
				}

				var minInt = (min is null ? int.MinValue : min.ToInt());
				var maxInt = (max is null ? int.MaxValue : max.ToInt());

				if (minInt == maxInt)
					return (long)minInt;

				if (minInt > maxInt)
					(minInt, maxInt) = (maxInt, minInt);

				// GetInt32's upper bound is exclusive where this function's is inclusive, so the range is shifted
				// by one at whichever end has room. When it has room at neither, every 32-bit value is in range
				// and four uniform bytes are the answer.
				if (maxInt < int.MaxValue)
					return (long)RandomNumberGenerator.GetInt32(minInt, maxInt + 1);

				if (minInt > int.MinValue)
					return (long)RandomNumberGenerator.GetInt32(minInt - 1, maxInt) + 1L;

				Span<byte> full = stackalloc byte[4];
				RandomNumberGenerator.Fill(full);
				return (long)BitConverter.ToInt32(full);
			}

			/// <summary>
			/// Calculates the SHA1 hash of a value.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">The value to hash, as <see cref="Hash"/> takes it.</param>
			/// <param name="encoding">The encoding a string <paramref name="value"/> is taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>A 40-character hexadecimal number.</returns>
			/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
			/// <exception cref="TypeError">Thrown if the value holds no bytes.</exception>
			/// <remarks>SHA1 is unsuitable for anything that must resist a deliberate collision.</remarks>
			[Static]
			public static object SHA1(object @this, object value, object encoding = null) => Hash(@this, value, "SHA1", encoding);

			/// <summary>
			/// Calculates the SHA256 hash of a value.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">The value to hash, as <see cref="Hash"/> takes it.</param>
			/// <param name="encoding">The encoding a string <paramref name="value"/> is taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>A 64-character hexadecimal number.</returns>
			/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
			/// <exception cref="TypeError">Thrown if the value holds no bytes.</exception>
			[Static]
			public static object SHA256(object @this, object value, object encoding = null) => Hash(@this, value, "SHA256", encoding);

			/// <summary>
			/// Calculates the SHA384 hash of a value.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">The value to hash, as <see cref="Hash"/> takes it.</param>
			/// <param name="encoding">The encoding a string <paramref name="value"/> is taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>A 96-character hexadecimal number.</returns>
			/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
			/// <exception cref="TypeError">Thrown if the value holds no bytes.</exception>
			[Static]
			public static object SHA384(object @this, object value, object encoding = null) => Hash(@this, value, "SHA384", encoding);

			/// <summary>
			/// Calculates the SHA512 hash of a value.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">The value to hash, as <see cref="Hash"/> takes it.</param>
			/// <param name="encoding">The encoding a string <paramref name="value"/> is taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>A 128-character hexadecimal number.</returns>
			/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
			/// <exception cref="TypeError">Thrown if the value holds no bytes.</exception>
			[Static]
			public static object SHA512(object @this, object value, object encoding = null) => Hash(@this, value, "SHA512", encoding);

			/// <summary>
			/// Every hash this class knows: the name a script writes, how to build it, and the name a key
			/// derivation asks for, which a checksum has none of. The error messages are spelled from this table
			/// so that adding a row cannot leave them claiming something else.
			/// </summary>
			/// <remarks>The tables come before the names spelled from them because a static initializer runs in
			/// the order it is written, so sorting this block would leave the names reading an empty table.</remarks>
			private static readonly (string Name, Func<HashAlgorithm> Create, HashAlgorithmName? Derivation)[] algorithms =
			[
				// Every factory is qualified because this class has a method of each of these names, which hides
				// the type name here.
				// MD5 hashes but does not derive: .NET's PBKDF2 rejects it on every platform, measured on Windows
				// and Linux alike, so offering it would only produce an error at the point of use.
				("MD5", System.Security.Cryptography.MD5.Create, null),
				("SHA1", System.Security.Cryptography.SHA1.Create, HashAlgorithmName.SHA1),
				("SHA256", System.Security.Cryptography.SHA256.Create, HashAlgorithmName.SHA256),
				("SHA384", System.Security.Cryptography.SHA384.Create, HashAlgorithmName.SHA384),
				("SHA512", System.Security.Cryptography.SHA512.Create, HashAlgorithmName.SHA512),
				("CRC32", () => new Keysharp.Internals.Cryptography.CRC32(), null),
			];

			/// <summary>
			/// The block chaining modes, which are the ones <see cref="TryGetCipherMode"/> resolves.
			/// </summary>
			private static readonly (string Name, CipherMode Mode)[] cipherModes =
			[
				("CBC", CipherMode.CBC),
				("ECB", CipherMode.ECB),
				("CFB", CipherMode.CFB),
			];

			/// <summary>
			/// The one mode that authenticates as well as encrypts, and so takes a different construction.
			/// </summary>
			private const string AeadMode = "GCM";

			/// <summary>
			/// The ciphers <see cref="Transform"/> knows, spelled for an error message.
			/// </summary>
			private const string CipherNames = "AES";

			/// <summary>
			/// SHA-256: the algorithm an integrity check should use.
			/// </summary>
			private const string DefaultAlgorithm = "SHA256";

			/// <summary>
			/// AES, the only cipher <see cref="Transform"/> builds.
			/// </summary>
			private const string DefaultCipher = "AES";

			/// <summary>
			/// CBC rather than ECB, which leaks where the plaintext repeats.
			/// </summary>
			private const string DefaultCipherMode = "CBC";

			/// <summary>
			/// The iteration count <see cref="PBKDF2"/> uses when a script names none: the figure OWASP gives for
			/// PBKDF2-HMAC-SHA256 as of 2023.
			/// </summary>
			private const long DefaultIterations = 600000;

			/// <summary>
			/// The length <see cref="PBKDF2"/> produces when a script names none, which is an AES-256 key.
			/// </summary>
			private const long DefaultKeyLength = 32;

			/// <summary>
			/// The tag <see cref="AeadMode"/> produces, and the shortest an authenticated message can be.
			/// </summary>
			private const int TagLength = 16;

			/// <summary>
			/// The algorithms <see cref="CreateAlgorithm"/> knows, spelled for an error message.
			/// </summary>
			private static string AlgorithmNames { get; } = Spell(algorithms.Select(a => a.Name));

			/// <summary>
			/// The modes <see cref="Transform"/> knows, spelled for an error message.
			/// </summary>
			private static string CipherModes { get; } = Spell(cipherModes.Select(m => m.Name).Append(AeadMode));

			/// <summary>
			/// The algorithms <see cref="DeriveKey"/> accepts, spelled for an error message.
			/// </summary>
			private static string DerivationNames { get; } = Spell(algorithms.Where(a => a.Derivation.HasValue).Select(a => a.Name));

			/// <summary>
			/// Encrypts or decrypts with AES-GCM, which authenticates what it encrypts.
			/// </summary>
			/// <param name="keyBytes">The key as the script gave it, sized here.</param>
			/// <param name="buffer">The data, which when decrypting ends with the authentication tag.</param>
			/// <param name="decrypt">true to decrypt, false to encrypt.</param>
			/// <param name="nonce">The nonce, or null to carry it with the ciphertext.</param>
			/// <returns>The transformed bytes, or null if anything was rejected.</returns>
			private static byte[] Authenticated(byte[] keyBytes, byte[] buffer, bool decrypt, byte[] nonce)
			{
				// Checked rather than assumed: the .NET implementation is backed by the platform's own crypto
				// library, which does not offer GCM everywhere.
				if (!AesGcm.IsSupported)
				{
					_ = Errors.ValueErrorOccurred("AES-GCM is not available on this platform.");
					return null;
				}

				var key = PadKey(keyBytes, 256 / 8);
				var nonceLength = AesGcm.NonceByteSizes.MaxSize;
				var carried = nonce == null;

				if (decrypt)
				{
					if (buffer.Length < (carried ? nonceLength : 0) + TagLength)
					{
						_ = Errors.ValueErrorOccurred("The data is too short to be an authenticated message.");
						return null;
					}

					if (carried)
					{
						nonce = buffer[..nonceLength];
						buffer = buffer[nonceLength..];
					}
				}
				else
					nonce ??= RandomNumberGenerator.GetBytes(nonceLength);

				if (nonce.Length != nonceLength)
				{
					_ = Errors.ValueErrorOccurred($"A nonce must be exactly {nonceLength} bytes, not {nonce.Length}.");
					return null;
				}

				using var gcm = new AesGcm(key, TagLength);

				if (decrypt)
				{
					var plain = new byte[buffer.Length - TagLength];

					try
					{
						gcm.Decrypt(nonce, buffer.AsSpan(0, plain.Length), buffer.AsSpan(plain.Length), plain);
					}
					catch (CryptographicException)
					{
						// The message is deliberately about authenticity rather than the key: a wrong key and an
						// altered message are the same event to GCM, and that is the point of using it.
						_ = Errors.ValueErrorOccurred("Decryption failed: the data does not authenticate, so it was encrypted under a different key or has been altered since.");
						return null;
					}

					return plain;
				}

				var offset = carried ? nonceLength : 0;
				var result = new byte[offset + buffer.Length + TagLength];

				if (carried)
					nonce.CopyTo(result, 0);

				gcm.Encrypt(nonce, buffer, result.AsSpan(offset, buffer.Length), result.AsSpan(offset + buffer.Length));
				return result;
			}

			/// <summary>
			/// Encrypts or decrypts with a block cipher in a chaining mode, which does not authenticate.
			/// </summary>
			/// <param name="keyBytes">The key as the script gave it, sized here to the cipher.</param>
			/// <param name="buffer">The data, which when decrypting begins with the vector unless one was supplied.</param>
			/// <param name="decrypt">true to decrypt, false to encrypt.</param>
			/// <param name="cipherMode">The chaining mode.</param>
			/// <param name="iv">The initialization vector, or null to carry it with the ciphertext.</param>
			/// <returns>The transformed bytes, or null if anything was rejected.</returns>
			private static byte[] Chained(byte[] keyBytes, byte[] buffer, bool decrypt, CipherMode cipherMode, byte[] iv)
			{
				using var alg = Aes.Create();
				alg.Mode = cipherMode;
				var size = 0;

				foreach (var legal in alg.LegalKeySizes)
					size = Math.Max(size, legal.MaxSize);

				alg.Key = PadKey(keyBytes, size / 8);
				var ivLength = alg.BlockSize / 8;
				var usesIV = cipherMode != CipherMode.ECB;
				// Only a vector this code generated travels with the ciphertext; one the caller supplied is one the
				// caller already has, and prepending it would corrupt a format they are matching.
				var carried = usesIV && iv == null;

				if (!usesIV)
				{
					if (iv != null)
					{
						_ = Errors.ValueErrorOccurred($"{cipherMode} uses no initialization vector.");
						return null;
					}
				}
				else if (carried)
				{
					// Carried with the ciphertext so that encrypting is safe without the caller knowing what a vector
					// is: a random one per message keeps equal plaintexts from encrypting alike, and it is not a
					// secret, so it can travel in front of the data it belongs to.
					if (!decrypt)
						iv = RandomNumberGenerator.GetBytes(ivLength);
					else if (buffer.Length < ivLength)
					{
						_ = Errors.ValueErrorOccurred($"The data is shorter than the {ivLength}-byte initialization vector it must begin with.");
						return null;
					}
					else
					{
						iv = buffer[..ivLength];
						buffer = buffer[ivLength..];
					}
				}

				if (usesIV)
				{
					if (iv.Length != ivLength)
					{
						_ = Errors.ValueErrorOccurred($"An initialization vector must be exactly {ivLength} bytes, not {iv.Length}.");
						return null;
					}

					alg.IV = iv;
				}

				byte[] result;

				try
				{
					// Creating the transform is inside the guard because it is where the platform's provider gets to
					// refuse a mode it does not implement, alongside the wrong key, truncated input and mismatched
					// mode that the transform itself rejects. None of those is an Error a script can catch.
					using var trans = decrypt ? alg.CreateDecryptor() : alg.CreateEncryptor();
					result = trans.TransformFinalBlock(buffer, 0, buffer.Length);
				}
				catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
				{
					_ = Errors.ValueErrorOccurred($"{(decrypt ? "Decryption" : "Encryption")} failed: {ex.Message}");
					return null;
				}

				if (decrypt || !carried)
					return result;

				var withIV = new byte[ivLength + result.Length];
				iv.CopyTo(withIV, 0);
				result.CopyTo(withIV, ivLength);
				return withIV;
			}

			/// <summary>
			/// Runs the symmetric cipher in one direction, resolving the names a script supplied.
			/// </summary>
			/// <returns>The result as a <see cref="Buffer"/>, or the empty string if the cipher, the mode or the
			/// transform was rejected and errors are not being thrown.</returns>
			private static object Cipher(object value, object key, bool decrypt, object algorithm, object mode, object iv, object encoding)
			{
				var enc = ResolveEncoding(encoding);
				byte[] ivBytes = null;

				if (!(iv is null || (iv is string s && s.Length == 0)))
					if ((ivBytes = Conversions.ToByteArray(iv, enc)) == null)
						return "";

				var result = Transform(value, key, decrypt, Named(algorithm, DefaultCipher), Named(mode, DefaultCipherMode), ivBytes, enc);
				return result == null ? "" : new Buffer(result);
			}

			/// <summary>
			/// Creates the hash algorithm a script asked for by name.
			/// </summary>
			/// <param name="name">The algorithm name, matched case-insensitively. An embedded "-" is ignored, so
			/// "SHA-256" — the spelling external tools print — resolves as well as "SHA256".</param>
			/// <returns>The algorithm, or null if the name is not one of <see cref="AlgorithmNames"/>.</returns>
			private static HashAlgorithm CreateAlgorithm(string name) => TryFind(name, out var entry) ? entry.Create() : null;

			/// <summary>
			/// Derives key material from a password with PBKDF2.
			/// </summary>
			/// <param name="password">The password to stretch.</param>
			/// <param name="salt">The salt, which need not be secret but must differ per password.</param>
			/// <param name="iterations">How many times to iterate. The cost of a guess rises with it.</param>
			/// <param name="length">How many bytes to produce.</param>
			/// <param name="algorithm">The hash to iterate, one of <see cref="DerivationNames"/>.</param>
			/// <param name="enc">The encoding a string password or salt is taken in.</param>
			/// <returns>The derived bytes, or null if anything was rejected.</returns>
			private static byte[] DeriveKey(object password, object salt, long iterations, long length, string algorithm, Encoding enc)
			{
				if (!TryFind(algorithm, out var entry) || !entry.Derivation.HasValue)
				{
					_ = Errors.ValueErrorOccurred($"Unknown derivation algorithm. Specify {DerivationNames}.", algorithm);
					return null;
				}

				if (iterations < 1)
				{
					_ = Errors.ValueErrorOccurred("The iteration count must be at least 1.", iterations);
					return null;
				}

				if (length < 1 || length > int.MaxValue)
				{
					_ = Errors.ValueErrorOccurred($"The length must be between 1 and {int.MaxValue}.", length);
					return null;
				}

				var secret = Conversions.ToByteArray(password, enc);
				var seasoning = Conversions.ToByteArray(salt, enc);

				if (secret == null || seasoning == null)
					return null;

				try
				{
					return Rfc2898DeriveBytes.Pbkdf2(secret, seasoning, (int)Math.Min(iterations, int.MaxValue), entry.Derivation.Value, (int)length);
				}
				catch (CryptographicException ex)
				{
					// The platform's crypto library has the last word on which hashes it will iterate, and a raw
					// CryptographicException is not an Error that a script can catch.
					_ = Errors.ValueErrorOccurred($"Key derivation failed: {ex.Message}");
					return null;
				}
			}

			/// <summary>
			/// Hashes a value, streaming it when it is a File so that a large one is never held in memory.
			/// </summary>
			/// <returns>The digest as uppercase hexadecimal.</returns>
			private static object Digest(object value, HashAlgorithm alg, Encoding enc)
			{
				if (value is not KeysharpFile file)
				{
					var raw = Conversions.ToByteArray(value, enc);
					return raw == null ? "" : HexDigest(raw, alg);
				}

				// Tested before flushing: a closed File still holds its closed stream, and flushing that throws an
				// ObjectDisposedException, which is not an Error a script can catch.
				var stream = file.BaseStream;

				if (stream == null || !stream.CanRead)
					return Errors.ValueErrorOccurred("The File passed to hash is not open for reading.", null, "");

				// Guarded as HashFile guards its own read: the stream can fail at any point here — a share that
				// drops, a disk that errors — and an IOException is not an Error a script can catch.
				try
				{
					_ = file.Flush();

					// Hashing a file means its whole content, so a seekable stream is rewound; one that cannot
					// seek can only be hashed from where it stands.
					if (!stream.CanSeek)
						return HexDigest(stream, alg);

					var pos = stream.Position;
					stream.Position = 0;

					try
					{
						return HexDigest(stream, alg);
					}
					finally
					{
						stream.Position = pos;
					}
				}
				catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException)
				{
					ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
					return Errors.OSErrorOccurred(ex, "Error reading the File passed to hash", "");
				}
			}

			/// <summary>
			/// Hashes bytes. The caller owns <paramref name="alg"/>.
			/// </summary>
			/// <returns>The digest as uppercase hexadecimal.</returns>
			private static string HexDigest(byte[] raw, HashAlgorithm alg) => Convert.ToHexString(alg.ComputeHash(raw));

			/// <summary>
			/// Hashes everything <paramref name="stream"/> still holds, without ever holding all of it in memory.
			/// The caller owns both the stream and <paramref name="alg"/>.
			/// </summary>
			/// <returns>The digest as uppercase hexadecimal.</returns>
			private static string HexDigest(Stream stream, HashAlgorithm alg) => Convert.ToHexString(alg.ComputeHash(stream));

			/// <summary>
			/// Resolves a name a script may have left blank, since an omitted argument reaches here as the empty
			/// string as readily as it does unset.
			/// </summary>
			private static string Named(object value, string def)
			{
				var name = value.As();
				return name.Length == 0 ? def : name;
			}

			/// <summary>
			/// Folds a name to the form the tables are written in.
			/// </summary>
			private static string Normalize(string name) => name.Replace("-", "").ToUpperInvariant();

			/// <summary>
			/// Sizes a key to the cipher, zero-padding a short one and ignoring anything past the end of a long one.
			/// The key is used as it stands rather than stretched, which is what <see cref="DeriveKey"/> is for.
			/// </summary>
			private static byte[] PadKey(byte[] keyBytes, int length)
			{
				var k = new byte[length];

				for (var i = 0; i < Math.Min(length, keyBytes.Length); i++)
					k[i] = keyBytes[i];

				return k;
			}

			/// <summary>
			/// Resolves the encoding a string value is taken in, defaulting to UTF-8.
			/// </summary>
			private static Encoding ResolveEncoding(object encoding) => Files.GetEncodingOrDefault(encoding, Encoding.UTF8);

			/// <summary>
			/// Writes a list of names the way an error message reads them out.
			/// </summary>
			private static string Spell(IEnumerable<string> names)
			{
				var all = names.ToArray();
				return all.Length == 1 ? all[0] : $"{string.Join(", ", all[..^1])} or {all[^1]}";
			}

			/// <summary>
			/// Encrypts or decrypts with a symmetric cipher, dispatching on the mode: the authenticated one is a
			/// different construction from the chaining ones, not a variation of them.
			/// </summary>
			/// <param name="value">The data to encrypt or decrypt.</param>
			/// <param name="key">The secret key.</param>
			/// <param name="decrypt">true to decrypt, false to encrypt.</param>
			/// <param name="cipherName">The cipher, one of <see cref="CipherNames"/>.</param>
			/// <param name="modeName">The mode, one of <see cref="CipherModes"/>.</param>
			/// <param name="iv">The initialization vector or nonce, or null to let it travel with the ciphertext:
			/// encrypting then draws a random one and writes it in front of the result, and decrypting reads it back
			/// off the front. A mode which uses none, such as ECB, accepts only null.</param>
			/// <param name="enc">The encoding a string value or key is taken in.</param>
			/// <returns>The transformed bytes, or null if anything was rejected.</returns>
			private static byte[] Transform(object value, object key, bool decrypt, string cipherName, string modeName, byte[] iv, Encoding enc)
			{
				if (Normalize(cipherName) != CipherNames)
				{
					_ = Errors.ValueErrorOccurred($"Unknown cipher. Specify {CipherNames}.", cipherName);
					return null;
				}

				var keyBytes = Conversions.ToByteArray(key, enc);
				var buffer = Conversions.ToByteArray(value, enc);

				if (keyBytes == null || buffer == null)
					return null;

				var mode = Normalize(modeName);

				if (mode == AeadMode)
					return Authenticated(keyBytes, buffer, decrypt, iv);

				if (!TryGetCipherMode(mode, out var cipherMode))
				{
					_ = Errors.ValueErrorOccurred($"Unknown cipher mode. Specify {CipherModes}.", modeName);
					return null;
				}

				return Chained(keyBytes, buffer, decrypt, cipherMode, iv);
			}

			/// <summary>
			/// Looks a hash algorithm up by the name a script wrote.
			/// </summary>
			private static bool TryFind(string name, out (string Name, Func<HashAlgorithm> Create, HashAlgorithmName? Derivation) found)
			{
				var normalized = Normalize(name);

				foreach (var entry in algorithms)
				{
					if (entry.Name == normalized)
					{
						found = entry;
						return true;
					}
				}

				found = default;
				return false;
			}

			/// <summary>
			/// Resolves a block chaining mode by name.
			/// </summary>
			private static bool TryGetCipherMode(string name, out CipherMode mode)
			{
				foreach (var entry in cipherModes)
				{
					if (entry.Name == Normalize(name))
					{
						mode = entry.Mode;
						return true;
					}
				}

				mode = default;
				return false;
			}

			/// <summary>
			/// Raises the error for an algorithm name that is not one of the supported ones.
			/// </summary>
			private static object UnknownAlgorithm(string name)
				=> Errors.ValueErrorOccurred($"Unknown hash algorithm. Specify one of {AlgorithmNames}.", name, "");
		}
	}
}
