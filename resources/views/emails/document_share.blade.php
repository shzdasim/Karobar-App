<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>{{ $subjectLine }}</title>
</head>
<body style="font-family: Arial, Helvetica, sans-serif; font-size: 14px; color: #1e293b; line-height: 1.6; background-color: #f8fafc; padding: 24px; margin: 0;">
    <div style="max-width: 600px; margin: 0 auto; background: #ffffff; border: 1px solid #e2e8f0; border-radius: 8px; overflow: hidden;">
        <div style="background: #0f172a; color: #ffffff; padding: 18px 24px;">
            <h2 style="margin: 0; font-size: 18px; font-weight: bold;">{{ $storeName }}</h2>
        </div>
        <div style="padding: 24px;">
            <div style="white-space: pre-line; color: #334155; font-size: 14px;">{{ $messageBody }}</div>

            <div style="margin-top: 20px; padding: 12px 16px; background: #f1f5f9; border-left: 4px solid #3b82f6; border-radius: 4px; font-size: 13px; color: #475569;">
                <strong>Attached PDF:</strong> {{ $pdfFilename }}
            </div>
        </div>
        <div style="padding: 14px 24px; background: #f8fafc; border-top: 1px solid #e2e8f0; font-size: 12px; color: #64748b; text-align: center;">
            Sent by {{ $storeName }}
        </div>
    </div>
</body>
</html>

