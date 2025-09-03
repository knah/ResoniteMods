using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ResoQuiccMk2.Utils;

public static class CertificateUtil
{
    public static X509Certificate2 GenerateSelfSignedCertificate(string subjectName, bool canSignCerts, bool server)
    {
        var secp256r1Oid = "1.2.840.10045.3.1.7";  //oid for prime256v1(7)  other identifier: secp256r1

        var ecdsa = ECDsa.Create(ECCurve.CreateFromValue(secp256r1Oid));

        var certRequest = new CertificateRequest($"CN={subjectName}", ecdsa, HashAlgorithmName.SHA256);

        certRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement | X509KeyUsageFlags.KeyEncipherment |
            (canSignCerts ? X509KeyUsageFlags.KeyCertSign : 0), true));
        certRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(canSignCerts, false, 0, true));
        certRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection()
                { Oid.FromOidValue(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2", OidGroup.EnhancedKeyUsage) },
            true));
        
        return certRequest.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));
    }

    public static string CertificateHashString(this X509Certificate cert)
    {
        return cert.GetCertHashString(HashAlgorithmName.SHA256);
    }

    public static X509ChainPolicy GetEmptyChainPolicy()
    {
        return new X509ChainPolicy
        {
            DisableCertificateDownloads = true,
            RevocationMode = X509RevocationMode.NoCheck,
            TrustMode = X509ChainTrustMode.CustomRootTrust,
        };
    }
}