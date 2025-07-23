using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace GameRes.Gx4Lib
{
    public class PackageFile
    {
        public PFHeaders Deserialize (Stream input)
        {
            var bin = new BinaryFormatter { Binder = new Gx4TypeBinder() };
            return bin.Deserialize (input) as PFHeaders;
        }
    }

    [Serializable]
    public class PFHeader
    {
        public string   FileName;
        public long     readStartBytePos;
        public long     ByteLength;
    }

    [Serializable]
    public class PFHeaders
    {
        public PFHeader[] headers;
    }

    [Serializable]
    public class PFAudioHeaders : PFHeaders
    {
    }

    [Serializable]
    public class PFImageHeaders : PFHeaders
    {
    }

    internal class Gx4TypeBinder : SerializationBinder
    {
        public override Type BindToType (string assemblyName, string typeName)
        {
            if ("GX4Lib" == assemblyName)
            {
                if (typeName.StartsWith ("GX4.PackageFile`1+PFHeaders[["))
                {
                    if (0 == string.Compare (typeName, 29, "UnityEngine.AudioClip", 0, 21))
                        return typeof(PFAudioHeaders);
                    if (0 == string.Compare (typeName, 29, "UnityEngine.Texture2D", 0, 21))
                        return typeof(PFImageHeaders);
                    return typeof(PFHeaders);
                }
                else if (typeName.StartsWith ("GX4.PackageFile`1+PFHeader[["))
                {
                    return typeof(PFHeader);
                }
            }
            return null;
        }
    }
}
