select tq."Order", q."Id", q."CorrectAnswerId", CASE WHEN q."CorrectAnswerId" = a."Id" THEN 'Correct' ELSE NULL END,
	 a.* from "TestQuestions" tq 
   join "Questions" q on tq."QuestionId" = q."Id"
   join "Answers" a on a."QuestionId" = q."Id"
	where tq."TestId" = 27
	-- and q."CorrectAnswerId" = a."Id"
	order by tq."Order", q."Id", a."Id"