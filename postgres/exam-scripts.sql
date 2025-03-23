select tq."Order", q."Id", q."CorrectAnswerId", CASE WHEN q."CorrectAnswerId" = a."Id" THEN 'Correct' ELSE NULL END,
	 a.* from "TestQuestions" tq 
   join "Questions" q on tq."QuestionId" = q."Id"
   join "Answers" a on a."QuestionId" = q."Id"
	where tq."TestId" = 27
	-- and q."CorrectAnswerId" = a."Id"
	order by tq."Order", q."Id", a."Id"

/*doğru cevapları listeleme*/
select tq."Order", q."Id", q."CorrectAnswerId", a."Id", a.* from "Worksheets" w
 join "TestQuestions" tq on tq."TestId" = w."Id"
 join "Questions" q on q."Id" = tq."QuestionId" 
 join "Answers" a on a."QuestionId" = q."Id" and q."CorrectAnswerId" = a."Id"
 where w."Subtitle" LIKE '%9%'  
 order by tq."Order", q."Id", a."Id"