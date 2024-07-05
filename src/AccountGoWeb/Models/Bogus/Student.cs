namespace AccountGoWeb.Models.Bogus;

public class Student
{
    required public int? Id { get; set; }
    required public string FirstName { get; set; }
    required public string LastName { get; set; }
    required public string School { get; set; }
    public static IQueryable<Student> GetStudents()
    {
        int ndx = 0;
        List<Student> students = new List<Student>() {
            new Student() { Id = ++ndx, FirstName="Max", LastName="Pao", School="Science" },
            new Student() { Id = ++ndx, FirstName="Tom", LastName="Fay", School="Mining" },
            new Student() { Id = ++ndx, FirstName="Ann", LastName="Sun", School="Nursing" },
            new Student() { Id = ++ndx, FirstName="Joe", LastName="Fox", School="Computing" },
            new Student() { Id = ++ndx, FirstName="Sue", LastName="Mai", School="Mining" },
            new Student() { Id = ++ndx, FirstName="Ben", LastName="Lau", School="Business" },
            new Student() { Id = ++ndx, FirstName="Zoe", LastName="Ray", School="Mining" },
            new Student() { Id = ++ndx, FirstName="Sam", LastName="Ash", School="Medicine" },
            new Student() { Id = ++ndx, FirstName="Dan", LastName="Lee", School="Computing" },
            new Student() { Id = ++ndx, FirstName="Pat", LastName="Day", School="Science" },
            new Student() { Id = ++ndx, FirstName="Kim", LastName="Rex", School="Computing" },
            new Student() { Id = ++ndx, FirstName="Tim", LastName="Ram", School="Business" },
            new Student() { Id = ++ndx, FirstName="Rob", LastName="Wei", School="Mining" },
            new Student() { Id = ++ndx, FirstName="Jan", LastName="Tex", School="Science" },
            new Student() { Id = ++ndx, FirstName="Jim", LastName="Kid", School="Business" },
            new Student() { Id = ++ndx, FirstName="Ben", LastName="Chu", School="Medicine" },
            new Student() { Id = ++ndx, FirstName="Mia", LastName="Tao", School="Computing" },
            new Student() { Id = ++ndx, FirstName="Ted", LastName="Day", School="Business" },
            new Student() { Id = ++ndx, FirstName="Amy", LastName="Roy", School="Science" },
            new Student() { Id = ++ndx, FirstName="Ian", LastName="Kit", School="Nursing" },
            new Student() { Id = ++ndx, FirstName="Liz", LastName="Tan", School="Medicine" },
            new Student() { Id = ++ndx, FirstName="Mat", LastName="Roy", School="Tourism" },
            new Student() { Id = ++ndx, FirstName="Deb", LastName="Luo", School="Medicine" },
            new Student() { Id = ++ndx, FirstName="Ana", LastName="Poe", School="Computing" },
            new Student() { Id = ++ndx, FirstName="Lyn", LastName="Raj", School="Science" },
            new Student() { Id = ++ndx, FirstName="Amy", LastName="Ash", School="Tourism" },
            new Student() { Id = ++ndx, FirstName="Kim", LastName="Kid", School="Mining" },
            new Student() { Id = ++ndx, FirstName="Bec", LastName="Fry", School="Nursing" },
            new Student() { Id = ++ndx, FirstName="Eva", LastName="Lap", School="Computing" },
            new Student() { Id = ++ndx, FirstName="Eli", LastName="Yim", School="Business" },
            new Student() { Id = ++ndx, FirstName="Sam", LastName="Hui", School="Science" },
            new Student() { Id = ++ndx, FirstName="Joe", LastName="Jin", School="Mining" },
            new Student() { Id = ++ndx, FirstName="Liz", LastName="Kuo", School="Agriculture" },
            new Student() { Id = ++ndx, FirstName="Ric", LastName="Mak", School="Tourism" },
            new Student() { Id = ++ndx, FirstName="Pam", LastName="Day", School="Computing" },
            new Student() { Id = ++ndx, FirstName="Stu", LastName="Gad", School="Business" },
            new Student() { Id = ++ndx, FirstName="Tom", LastName="Bee", School="Tourism" },
            new Student() { Id = ++ndx, FirstName="Bob", LastName="Lam", School="Agriculture" },
            new Student() { Id = ++ndx, FirstName="Jim", LastName="Ots", School="Medicine" },
            new Student() { Id = ++ndx, FirstName="Tom", LastName="Mag", School="Mining" },
            new Student() { Id = ++ndx, FirstName="Hal", LastName="Doe", School="Agriculture" },
            new Student() { Id = ++ndx, FirstName="Roy", LastName="Kim", School="Nursing" },
            new Student() { Id = ++ndx, FirstName="Vis", LastName="Cox", School="Science" },
            new Student() { Id = ++ndx, FirstName="Kay", LastName="Aga", School="Tourism" },
            new Student() { Id = ++ndx, FirstName="Reo", LastName="Hui", School="Business" },
            new Student() { Id = ++ndx, FirstName="Bob", LastName="Roe", School="Medicine" },
        };
        return students.AsQueryable();
    }
}
