
DELETE FROM public."Questions" CASCADE;

DELETE FROM public."Answers" CASCADE;

DELETE FROM public."Worksheets" CASCADE;

DELETE FROM public."Passage" CASCADE;

DELETE FROM public."TestInstances" CASCADE;

DELETE FROM public."TestInstanceQuestions" CASCADE;

DELETE FROM public."TestQuestions" CASCADE;

DELETE FROM public."Books" CASCADE;

DELETE FROM public."BookTests" CASCADE;

DELETE FROM public."QuestionTransferExportBundles" CASCADE;

DELETE FROM public."QuestionTransferExportMaps" CASCADE;

DELETE FROM public."QuestionTransferImportMaps" CASCADE;

DELETE FROM public."QuestionTransferJobs" CASCADE;