﻿using System.Data.SQLite;

namespace ANWI.Database.Model
{
    /// <summary>
    /// Represents a row of the Rates table.
    /// </summary>

    public class Rate
    {
        public static Rate Factory()
        {
            Rate result = new Rate(-1, "", "", "");
            return result;
        }

        public static Rate Factory(int _id, string _name, string _abbreviation, string _icon_name)
        {
            Rate result = new Rate(_id, _name, _abbreviation, _icon_name);
            return result;
        }

        public static Rate Factory(SQLiteDataReader reader)
        {
            Rate result = new Rate(
                (int)reader["id"],
                (string)reader["name"],
                (string)reader["abbreviation"],
                (string)reader["icon_name"]
            );
            return result;
        }

        public int id;
        public string name;
        public string abbreviation;
        public string icon_name;

        private Rate(int _id, string _name, string _abbreviation, string _icon_name)
        {
            id = _id;
            name = _name;
            abbreviation = _abbreviation;
            icon_name = _icon_name;
        }
    }
}
