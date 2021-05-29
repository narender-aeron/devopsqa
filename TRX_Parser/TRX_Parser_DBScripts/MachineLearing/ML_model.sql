USE AQAResults;
--Create a table to store the model
DROP TABLE IF EXISTS errorClass_models;
GO
CREATE TABLE errorClass_models (
	model_name VARCHAR(30) NOT NULL DEFAULT('default model') PRIMARY KEY,
	model VARBINARY(MAX) NOT NULL
);
GO

--Stored procedure that trains and generates a Python model
DROP PROCEDURE IF EXISTS generate_errorClass_models;
go
CREATE PROCEDURE generate_errorClass_models (@trained_model varbinary(MAX) OUTPUT)
AS
BEGIN
    EXECUTE sp_execute_external_script
      @language = N'Python'
    , @script = N'
import pandas as pda
import pandas.io.sql as pd
import pyodbc
from sklearn.model_selection import train_test_split
from sklearn.feature_extraction.text import TfidfVectorizer
import sklearn
import revoscalepy
from sklearn.naive_bayes import MultinomialNB
import re
import nltk.corpus
nltk.download("stopwords")
from nltk.corpus import stopwords
import pickle

stop = stopwords.words("english")
train_data= errordata

def clean_text(df, text_field, new_text_field_name):
    df[new_text_field_name] = df[text_field].str.lower()
    df[new_text_field_name] = df[new_text_field_name].apply(lambda elem: re.sub(r"(@[A-Za-z0-9]+)|([^0-9A-Za-z \t])|(\w+:\/\/\S+)|^rt|http.+?", "", elem))  
    # remove numbers
    df[new_text_field_name] = df[new_text_field_name].apply(lambda elem: re.sub(r"\d+", "", elem))
    return df

data_clean = clean_text(train_data, "errorMessage", "errorMessage")
data_clean[''errorMessage''] = data_clean[''errorMessage''].apply(lambda x: " ".join([word for word in x.split() if word not in (stop)]))

X_train, X_test, Y_train, Y_test = train_test_split(data_clean["errorMessage"],data_clean["errorClass"],train_size=0.80, random_state=20)

vectorizer = TfidfVectorizer()
train_vectors = vectorizer.fit_transform(X_train)
test_vectors = vectorizer.transform(X_test)

vectorizerPath = "C:\\vectors\\pickles.pickle"
f = open(vectorizerPath, "wb")
pickle.dump(vectorizer, f)

model = MultinomialNB().fit(train_vectors, Y_train)
trained_model = pickle.dumps(model)'

, @input_data_1 = N'SELECT errorMessage, errorClass FROM failed_tests'
, @input_data_1_name = N'errordata'
, @params = N'@trained_model varbinary(MAX) OUTPUT'
, @trained_model = @trained_model OUTPUT;

END;
GO

--Save model to table
TRUNCATE TABLE errorClass_models;
DECLARE @model VARBINARY(MAX);
EXEC generate_errorClass_models @model OUTPUT;
INSERT INTO errorClass_models (model_name, model) VALUES('MultinomialNB', @model);


--Stored procedure that calls Python model to make a prediction on the data
DROP PROCEDURE IF EXISTS py_predict_errorclass;
GO
CREATE PROCEDURE py_predict_errorclass (@model varchar(100))
AS
BEGIN
	DECLARE @py_model varbinary(max) = (select model from errorClass_models where model_name = @model);
	EXEC sp_execute_external_script
				@language = N'Python',
				@script = N'
import pickle
import pandas as pd
import re
import nltk.corpus
from nltk.corpus import stopwords
nltk.download("stopwords")
from sklearn.model_selection import train_test_split # to split dataset
from sklearn.feature_extraction.text import TfidfVectorizer

MultinomialNB = pickle.loads(py_model)
stop = stopwords.words("english")

df = sample

def clean_text(df, text_field, new_text_field_name):
    df[new_text_field_name] = df[text_field].str.lower()
    df[new_text_field_name] = df[new_text_field_name].apply(lambda elem: re.sub(r"(@[A-Za-z0-9]+)|([^0-9A-Za-z \t])|(\w+:\/\/\S+)|^rt|http.+?", "", elem))  
    # remove numbers
    df[new_text_field_name] = df[new_text_field_name].apply(lambda elem: re.sub(r"\d+", "", elem))
    
    return df

data_clean = clean_text(df, "errorMessage", "errorMessage")
data_clean["errorMessage"] = data_clean["errorMessage"].apply(lambda x: " ".join([word for word in x.split() if word not in (stop)]))

vectorizerPath = "C:\\vectors\\pickles.pickle"
tfidf = pickle.load(open(vectorizerPath, "rb"))
tfidf = tfidf.transform(data_clean["errorMessage"])

NB_predictions = MultinomialNB.predict(tfidf)
predictions_df = pd.DataFrame(NB_predictions)

OutputDataSet = predictions_df.rename(columns={predictions_df.columns[0]: "predictedError"})
'
, @input_data_1 = N'SELECT TOP 1 errorMessage FROM result_fields ORDER BY testresultid DESC' 
, @input_data_1_name = N'sample'
, @params = N'@py_model varbinary(max)'
, @py_model = @py_model
with result sets (( errorClass VARCHAR));
END;
GO

EXEC py_predict_errorclass 'MultinomialNB';




