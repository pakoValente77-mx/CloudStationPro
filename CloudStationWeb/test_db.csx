using System;
using Microsoft.Data.SqlClient;

string connectionString = "Server=localhost;Database=CloudStationDB;User Id=sa;Password=MyP@ssword123;TrustServerCertificate=True;";
using (SqlConnection connection = new SqlConnection(connectionString))
{
    connection.Open();
    SqlCommand command = new SqlCommand("SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%Grupo%'", connection);
    using (SqlDataReader reader = command.ExecuteReader())
    {
        while (reader.Read())
        {
            Console.WriteLine($"{reader["TABLE_SCHEMA"]}.{reader["TABLE_NAME"]}");
        }
    }
}
