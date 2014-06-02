using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Jose
{
    public class AesKeyWrapManagement:IKeyManagement
    {
        private static readonly byte[] DefaultIV = { 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6 }; // http://www.ietf.org/rfc/rfc3394.txt (see 2.2.3)

        private int kekLengthBits;

        public AesKeyWrapManagement(int kekLengthBits)
        {
            this.kekLengthBits = kekLengthBits;
        }

        public byte[] NewKey(int keyLength, object key, IDictionary<string, object> header)
        {
            return Arrays.Random(keyLength);
        }

        public byte[] Wrap(byte[] cek, object key)
        {
            var sharedKey = Ensure.Type<byte[]>(key, "AesKeyWrap management algorithm expectes key to be byte[] array.");
            Ensure.BitSize(sharedKey, kekLengthBits, string.Format("AesKeyWrap management algorithm expected key of size {0} bits, but was given {1} bits", kekLengthBits, sharedKey.Length * 8));
            Ensure.MinBitSize(cek, 128, "AesKeyWrap management algorithm expects content length not less than 128 bits, but was {0}",cek.Length * 8);
            Ensure.Divisible(cek.Length, 8, "AesKeyWrap management algorithm expects content length to be divisable by 8, but was given a content of {0} bit size.", cek.Length * 8);

                                                        // 1) Initialize variables
            byte[] a = DefaultIV;                       // Set A = IV, an initial value
            byte[][] r = Arrays.Slice(cek, 8);          // For i = 1 to n
                                                        //     R[0][i] = P[i]
            long n = r.Length;
                                                                              // 2) Calculate intermediate values.
            for (long j = 0; j < 6; j++)                                      // For j = 0 to 5
            {
                for (long i = 0; i < n; i++)                                  //    For i=1 to n
                {
                    long t = n * j + i + 1;

                    byte[] b = AesEnc(sharedKey, Arrays.Concat(a, r[i]));     //      B=AES(K, A | R[i])
                    a = Arrays.FirstHalf(b);                                  //      A=MSB(64,B) ^ t where t = (n*j)+i
                    r[i] = Arrays.SecondHalf(b);                              //      R[i] = LSB(64, B)

                    a = Arrays.Xor(a, t);
                }
            }
                                                           // 3) Output the results
            byte[][] c = new byte[n + 1][];
            c[0] = a;                                     //  Set C[0] = A
            for (long i = 1; i <= n; i++)                 //  For i = 1 to n
                c[i] = r[i - 1];                          //     C[i] = R[i]

            return Arrays.Concat(c);
        }

        public byte[] Unwrap(byte[] encryptedCek, object key, int cekSizeBits, IDictionary<string, object> header)
        {
            var sharedKey = Ensure.Type<byte[]>(key, "AesKeyWrap management algorithm expectes key to be byte[] array.");
            Ensure.BitSize(sharedKey, kekLengthBits, string.Format("AesKeyWrap management algorithm expected key of size {0} bits, but was given {1} bits", kekLengthBits, sharedKey.Length * 8));
            Ensure.MinBitSize(encryptedCek, 128, "AesKeyWrap management algorithm expects content length not less than 128 bits, but was {0}", encryptedCek.Length * 8);
            Ensure.Divisible(encryptedCek.Length, 8, "AesKeyWrap management algorithm expects content length to be divisable by 8, but was given a content of {0} bit size.",encryptedCek.Length * 8);

                                                       // 1) Initialize variables
            byte[][] c = Arrays.Slice(encryptedCek, 8);
            byte[] a = c[0];                           //   Set A = C[0]
            byte[][] r = new byte[c.Length - 1][];

            for (int i = 1; i < c.Length; i++)         //   For i = 1 to n
                r[i - 1] = c[i];                       //       R[i] = C[i]

            long n = r.Length;
                                                                            // 2) Calculate intermediate values
            for (long j = 5; j >= 0; j--)                                   // For j = 5 to 0
            {
                for (long i = n - 1; i >= 0; i--)                           //   For i = n to 1
                {
                    long t = n * j + i + 1;

                    a = Arrays.Xor(a, t);
                    byte[] B = AesDec(sharedKey, Arrays.Concat(a, r[i]));     //     B = AES-1(K, (A ^ t) | R[i]) where t = n*j+i
                    a = Arrays.FirstHalf(B);                                  //     A = MSB(64, B)
                    r[i] = Arrays.SecondHalf(B);                              //     R[i] = LSB(64, B)
                }
            }

                                                            // 3) Output the results
            if (!Arrays.ConstantTimeEquals(DefaultIV, a))   // If A is an appropriate initial value 
                throw new IntegrityException("AesKeyWrap integrity check failed.");

                                                            // For i = 1 to n
            return Arrays.Concat(r);                        //    P[i] = R[i]
        }

        private static byte[] AesDec(byte[] sharedKey, byte[] cipherText)
        {
            using (Aes aes = new AesManaged())
            {
                aes.Key = sharedKey;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;

                using (MemoryStream ms = new MemoryStream())
                {
                    using (ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    {
                        using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
                        {
                            cs.Write(cipherText, 0, cipherText.Length);
                            cs.FlushFinalBlock();

                            return ms.ToArray();
                        }
                    }
                }
            }
        }

        private static byte[] AesEnc(byte[] sharedKey, byte[] plainText)
        {
            using (Aes aes = new AesManaged())
            {
                aes.Key = sharedKey;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;

                using (MemoryStream ms = new MemoryStream())
                {
                    using (ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                    {
                        using (CryptoStream encrypt = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                        {
                            encrypt.Write(plainText, 0, plainText.Length);
                            encrypt.FlushFinalBlock();

                            return ms.ToArray();
                        }
                    }
                }
            }
        }
    }
}