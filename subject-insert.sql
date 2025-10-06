


insert into public."SubTopics"
    ("TopicId","Name")
select st."TopicId", 'Olayların Oluş Sırası'
from public."GradeSubjects" gs
    join public."Grades" g on g."Id" = gs."GradeId"
    join public."Subjects" s on s."Id" = gs."SubjectId"
    join public."Topics" t on t."SubjectId" = s."Id" and t."GradeId" = g."Id"
    join public."SubTopics" st on st."TopicId" = t."Id"
where g."Name" = '3. Sınıf' and s."Name" = 'Türkçe' and t."Name" = 'Okuma'
LIMIT 1
		


	