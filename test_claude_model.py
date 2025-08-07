import os
import sys
import anthropic

def test_model_availability():
    try:
        # APIキーの取得
        api_key = os.environ.get("ANTHROPIC_API_KEY")
        if not api_key:
            print("Error: ANTHROPIC_API_KEY environment variable is not set")
            return False
            
        # Anthropicクライアントの初期化
        client = anthropic.Anthropic(api_key=api_key)
        
        # シンプルなリクエストでテスト
        model_name = "claude-3-7-sonnet-20250219"
        print(f"Testing model: {model_name}")
        
        # モデルでシンプルなリクエストをテスト
        message = client.messages.create(
            model=model_name,
            max_tokens=100,
            messages=[
                {"role": "user", "content": "Hello, are you available?"}
            ]
        )
        
        print("Model test successful!")
        print(f"Response: {message.content[0].text}")
        return True
        
    except Exception as e:
        print(f"Error testing model: {str(e)}")
        return False

if __name__ == "__main__":
    success = test_model_availability()
    sys.exit(0 if success else 1)
