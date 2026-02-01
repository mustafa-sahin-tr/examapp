


DO $$
DECLARE
    v_max_id INTEGER;
	v_passage_id INTEGER;
BEGIN
    -- PassageId değeri dolu (NULL olmayan) Questions kayıtlarından en büyük Id'yi bul
    SELECT MAX("Id")
    INTO v_max_id
    FROM public."Questions"
    WHERE "PassageId" IS NOT NULL;

	SELECT "PassageId" into v_passage_id FROM public."Questions" WHERE "Id" = v_max_id;

    -- İstediğiniz yerde bu değişkeni kullanabilirsiniz
    DELETE FROM public."TestQuestions" where "QuestionId" = v_max_id;
	DELETE FROM public."Questions" where "Id" = v_max_id;
	Update public."Questions" SET "PassageId" = v_passage_id, "ShowPassageFirst" = TRUE where "Id" > v_max_id;
	
END $$;

