using OfficeOpenXml;
using System;

class Program
{
    static void Main()
    {
        // Try to find the correct API
        // ExcelPackage.LicenseContext = LicenseContext.NonCommercial; // Obsolete
        
        // Method 1?
        // ExcelPackage.License = LicenseContext.NonCommercial; // Read-only error
        
        // Method 2?
        // ExcelPackage.License.SetLicense(LicenseContext.NonCommercial);
        
        Console.WriteLine("Properties of EPPlusLicense:");
        // Assuming EPPlusLicense is in OfficeOpenXml namespace, let's find it type first
        var licenseProp = typeof(OfficeOpenXml.ExcelPackage).GetProperty("License");
        var licenseType = licenseProp.PropertyType;
        Console.WriteLine("Type: " + licenseType.FullName);
        
        foreach(var prop in licenseType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            Console.WriteLine(prop.Name + " (" + prop.PropertyType.Name + ")");
        }
        
        foreach(var method in licenseType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!method.IsSpecialName && method.DeclaringType == licenseType)
                Console.WriteLine("Method: " + method.Name);
        }
    }
}
