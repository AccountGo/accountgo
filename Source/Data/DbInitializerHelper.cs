using Core.Domain;
using Core.Domain.Financials;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Data;

namespace Data
{
    public static class DbInitializerHelper
    {
        public static Company CreateDefaultCompany()
        {
            var company = new Company()
            {
                Name = "Financial Solutions Inc.",
                CompanyCode = "100",
                ShortName = "FSI",
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };

            return company;
        }

        public static List<Account> LoadChartOfAccountsFromFile(string filename)
        {
            string path = AppDomain.CurrentDomain.BaseDirectory + "/App_Data/" + filename;
            DataTable csvData = new DataTable();
            using (TextFieldParser csvReader = new TextFieldParser(path))
            {
                csvReader.SetDelimiters(new string[] { "," });
                csvReader.HasFieldsEnclosedInQuotes = true;
                string[] colFields = csvReader.ReadFields();
                foreach (string column in colFields)
                {
                    DataColumn datecolumn = new DataColumn(column);
                    datecolumn.AllowDBNull = true;
                    csvData.Columns.Add(datecolumn);
                }
                while (!csvReader.EndOfData)
                {
                    string[] fieldData = csvReader.ReadFields();
                    for (int i = 0; i < fieldData.Length; i++)
                    {
                        if (fieldData[i] == "")
                        {
                            fieldData[i] = null;
                        }
                    }
                    csvData.Rows.Add(fieldData);
                }
                List<Account> accounts = new List<Account>();
                foreach (DataRow row in csvData.Rows)
                {
                    Account account = new Account();
                    account.AccountCode = row["AccountCode"].ToString();
                    account.AccountName = row["AccountName"].ToString();
                    account.AccountClassId = int.Parse(row["AccountClass"].ToString());
                    account.CreatedBy = "System";
                    account.CreatedOn = DateTime.Now;
                    account.ModifiedBy = "System";
                    account.ModifiedOn = DateTime.Now;
                    accounts.Add(account);
                }

                return accounts;
            }
        }
    }
}
